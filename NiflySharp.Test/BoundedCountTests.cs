using NiflySharp.Blocks;
using NiflySharp.Enums;
using NiflySharp.Structs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace NiflySharp.Test
{
    /// <summary>
    /// A count or size in a malformed file that cannot fit in the file throws InvalidDataException before anything is
    /// allocated for it, and a block that reads past its stored size throws. Each input is a small authored SE mesh
    /// with a few bytes changed; before these bounds, each corrupt count allocated gigabytes before reading anything.
    /// </summary>
    public class BoundedCountTests
    {
        [Fact(DisplayName = "Four corrupted bytes that misalign the blocks throw a block overrun")]
        public void FourByteCorruption_ThrowsBlockOverrun()
        {
            var bytes = Corrupt((948, 0xC0), (756, 0xB1), (719, 0x4C), (385, 0x75));

            var ex = Assert.Throws<InvalidDataException>(() => Load(bytes));

            Assert.Contains("Block 0 (NiNode) read 556 bytes, past its stored size of 88", ex.Message);
        }

        [Fact(DisplayName = "A corrupted effect count that runs the root node past its size throws a block overrun")]
        public void EffectCountByte_ThrowsBlockOverrun()
        {
            // Byte 385 is the low byte of the root node's effect count; 0x75 makes it 117 refs, 468 bytes past its end.
            var bytes = Corrupt((385, 0x75));

            var ex = Assert.Throws<InvalidDataException>(() => Load(bytes));

            Assert.Contains("Block 0 (NiNode) read 556 bytes, past its stored size of 88", ex.Message);
        }

        [Fact(DisplayName = "A header block count larger than the file throws")]
        public void HeaderBlockCount_Throws()
        {
            var bytes = BuildSyntheticSe();
            // After the version line: file version (4), endian (1), user version (4), then the block count.
            int blockCountAt = Array.IndexOf(bytes, (byte)'\n') + 1 + 4 + 1 + 4;
            BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, blockCountAt);

            var ex = Assert.Throws<InvalidDataException>(() => Load(bytes));

            Assert.Contains($"The header's block count of {int.MaxValue}", ex.Message);
        }

        [Fact(DisplayName = "A block that reads more than its stored size throws")]
        public void BlockLongerThanStoredSize_Throws()
        {
            var bytes = BuildSyntheticSe();
            int sizeAt = BlockSizeOffset(bytes, 0);
            Assert.Equal(88, BitConverter.ToInt32(bytes, sizeAt));
            BitConverter.GetBytes(40).CopyTo(bytes, sizeAt);

            var ex = Assert.Throws<InvalidDataException>(() => Load(bytes));

            Assert.Contains("Block 0 (NiNode) read 88 bytes, past its stored size of 40", ex.Message);
        }

        [Fact(DisplayName = "An unknown block whose stored size is larger than the file throws before allocating")]
        public void UnknownBlockSize_ThrowsBeforeAllocating()
        {
            var bytes = BuildSyntheticSe();
            var nif = new NifFile();
            using (var ms = new MemoryStream(bytes, writable: false))
                Assert.Equal(0, nif.Load(ms));
            int alphaId = nif.Blocks.FindIndex(b => b is NiAlphaProperty);
            int sizeAt = BlockSizeOffset(bytes, alphaId);

            // Rename the type in the header's type table so the block reads as unknown, then give it a huge size.
            var name = System.Text.Encoding.ASCII.GetBytes("NiAlphaProperty");
            int nameAt = bytes.AsSpan().IndexOf(name);
            bytes[nameAt + name.Length - 1] = (byte)'X';
            BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, sizeAt);

            var ex = Assert.Throws<InvalidDataException>(() => Load(bytes));

            Assert.Contains($"Block {alphaId} (NiAlphaPropertX) is a type this library does not know, and its stored size of {int.MaxValue}", ex.Message);
        }

        [Fact(DisplayName = "A list count larger than the file throws before allocating")]
        public void ListCount_ThrowsBeforeAllocating()
        {
            // Byte 308 is the high byte of the root node's extra data count; 0x40 makes it 1,073,741,824 refs.
            var bytes = Corrupt((308, 0x40));

            var ex = Assert.Throws<InvalidDataException>(() => Load(bytes));

            Assert.Contains("Block 0 (NiNode): A list count of 1073741824", ex.Message);
        }

        [Fact(DisplayName = "The unchanged mesh still loads")]
        public void UnchangedMesh_Loads()
        {
            Assert.Equal(0, Load(BuildSyntheticSe()));
        }

        static int Load(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes, writable: false);
            return new NifFile().Load(ms);
        }

        static byte[] Corrupt(params (int Pos, byte Val)[] edits)
        {
            var bytes = BuildSyntheticSe();
            Assert.Equal(998, bytes.Length);
            foreach (var (pos, val) in edits)
                bytes[pos] = val;
            return bytes;
        }

        // Where block <paramref name="blockId"/>'s stored size sits: the header holds every block's size as one run of int32s.
        static int BlockSizeOffset(byte[] bytes, int blockId)
        {
            var nif = new NifFile();
            using (var ms = new MemoryStream(bytes, writable: false))
                Assert.Equal(0, nif.Load(ms));

            var table = Enumerable.Range(0, nif.Header.BlockCount).SelectMany(i => BitConverter.GetBytes(nif.Header.GetBlockSize(i))).ToArray();
            int tableAt = bytes.AsSpan().IndexOf(table);
            Assert.True(tableAt > 0);
            return tableAt + 4 * blockId;
        }

        // A small Skyrim SE mesh: a root node with two child nodes, a shape with alpha, a lighting shader with a texture
        // set, and a dismember skin instance. The byte positions above are offsets into this exact output.
        static byte[] BuildSyntheticSe()
        {
            var ver = new NiVersion { FileVersion = NiVersion.ToFile("20.2.0.7"), UserVersion = 12, StreamVersion = 100 };
            var f = new NifFile();
            f.Create(ver, withRootNode: true);
            var root = f.GetRootNodes().First();
            root.Name = new NiStringRef("GuardRoot");
            root.Flags_ui = 0xE;

            var childA = new NiNode { Name = new NiStringRef("GuardChildA"), Flags_ui = 0x40000E };
            root.Children.AddBlockRef(f.AddBlock(childA));
            var childB = new NiNode { Name = new NiStringRef("GuardChildB"), Flags_ui = 0x408000E };
            childA.Children.AddBlockRef(f.AddBlock(childB));

            var shape = new BSTriShape { Name = new NiStringRef("GuardShape"), Flags_ui = 0x400000E, Scale = 1.25f };
            root.Children.AddBlockRef(f.AddBlock(shape));

            var alpha = new NiAlphaProperty { Threshold = 128 };
            alpha.Flags.Value = 0x12ED;
            shape.AlphaPropertyRef = new NiBlockRef<NiAlphaProperty>(f.AddBlock(alpha));

            var shader = new BSLightingShaderProperty
            {
                Type = Helpers.ShaderHelper.ShaderGameType.SK,
                ShaderType_SK_FO4 = BSLightingShaderType.SkinTint,
                ShaderFlags_SSPF1 = SkyrimShaderPropertyFlags1.Specular | SkyrimShaderPropertyFlags1.Skinned | SkyrimShaderPropertyFlags1.Model_Space_Normals,
                ShaderFlags_SSPF2 = SkyrimShaderPropertyFlags2.ZBuffer_Write | SkyrimShaderPropertyFlags2.Double_Sided | SkyrimShaderPropertyFlags2.Soft_Lighting,
                EmissiveColor = new Color4 { R = 0.25f, G = 0.5f, B = 0.75f, A = 1f },
                EmissiveMultiple = 2.5f,
                Glossiness = 30f,
                SpecularStrength = 1.5f,
                Alpha = 0.5f,
                SpecularColor = new Color3 { R = 1f, G = 0.5f, B = 0.25f },
            };
            var texSet = new BSShaderTextureSet
            {
                Textures = new List<NiString4>
                {
                    new(@"textures\guard\diffuse.dds", false), new(@"textures\guard\normal.dds", false),
                    new(@"textures\guard\soft.dds", false),    new("", false),
                    new(@"textures\guard\slot4.dds", false),   new("", false),
                    new("", false),                            new(@"textures\guard\slot7.dds", false),
                    new("", false),
                },
            };
            texSet.NumTextures = (uint)texSet.Textures.Count;
            shader.TextureSetRef = new NiBlockRef<BSShaderTextureSet>(f.AddBlock(texSet));
            shape.ShaderPropertyRef = new NiBlockRef<BSShaderProperty>(f.AddBlock(shader));

            var skin = new BSDismemberSkinInstance
            {
                Partitions = new List<BodyPartList>
                {
                    new() { BodyPart = (BSDismemberBodyPartType)30, PartFlag = (BSPartFlag)257 },
                    new() { BodyPart = (BSDismemberBodyPartType)31, PartFlag = (BSPartFlag)257 },
                },
            };
            skin.NumPartitions = (uint)skin.Partitions.Count;
            shape.SkinInstanceRef = new NiBlockRef<NiObject>(f.AddBlock(skin));

            using var ms = new MemoryStream();
            Assert.Equal(0, f.Save(ms));
            return ms.ToArray();
        }
    }
}
