using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class BlueprintFileTests
    {
        private static readonly string[] Sample =
        {
            "#Name:Little hut",
            "#Creator:DaAlmyte",
            "#Description:\"A \\\"cosy\\\" hut\"",
            "#Category:Misc",
            "#SnapPoints",
            "0;0;0",
            "#Terrain",
            "circle;0;0;0;2;0",
            "#Pieces",
            "vc_hut_lumberjack;Misc;0;0;0;0;0;0;1;\"\";1;1;1",
            "wood_floor;Building;-1;0;1;0;0;0;1;\"\";1;1;1",
            "woodwall;Building;2;0;1;0;0.7071068;0;0.7071068;\"\"",
        };

        [Fact]
        public void Reads_the_header_and_only_the_pieces_section()
        {
            BlueprintFile bp = BlueprintFile.Parse(Sample);
            Assert.Equal("Little hut", bp.Name);
            Assert.Equal("DaAlmyte", bp.Creator);
            Assert.Equal("A \"cosy\" hut", bp.Description);
            Assert.Equal("Misc", bp.Category);
            Assert.Equal(new[] { "vc_hut_lumberjack", "wood_floor", "woodwall" }, bp.Pieces.Select(p => p.Name));
            Assert.Empty(bp.Problems);
        }

        [Fact]
        public void Reads_positions_rotations_and_a_missing_scale_as_one()
        {
            BlueprintPiece wall = BlueprintFile.Parse(Sample).Pieces[2];
            Assert.Equal(2f, wall.Position.X);
            Assert.Equal(1f, wall.Position.Z);
            Assert.Equal(0.7071068f, wall.Rotation.Y, 5);
            Assert.Equal(1f, wall.Scale.X);
        }

        [Fact]
        public void Windows_line_ends_a_byte_order_mark_and_decimal_commas_read_the_same()
        {
            string[] messy =
            {
                "﻿#Name:Little hut\r",
                "#Pieces\r",
                "wood_floor;Building;-1,5;0;1,25;0;0;0;1;\"\";1;1;1\r",
                "",
            };
            BlueprintFile bp = BlueprintFile.Parse(messy);
            Assert.Equal("Little hut", bp.Name);
            Assert.Single(bp.Pieces);
            Assert.Equal(-1.5f, bp.Pieces[0].Position.X);
            Assert.Equal(1.25f, bp.Pieces[0].Position.Z);
            Assert.Empty(bp.Problems);
        }

        [Fact]
        public void A_broken_line_is_skipped_and_reported_with_its_number()
        {
            BlueprintFile bp = BlueprintFile.Parse(new[] { "#Pieces", "wood_floor;Building;x;0;0;0;0;0;1", "woodwall;Building;0;0;0;0;0;0;1" });
            Assert.Single(bp.Pieces);
            Assert.Single(bp.Problems);
            Assert.StartsWith("line 2:", bp.Problems[0]);
        }

        [Fact]
        public void Old_files_without_a_pieces_header_still_read()
        {
            BlueprintFile bp = BlueprintFile.Parse(new[] { "wood_floor;Building;0;0;0;0;0;0;1" });
            Assert.Single(bp.Pieces);
        }

        [Fact]
        public void Writing_and_reading_back_keeps_everything()
        {
            BlueprintFile original = BlueprintFile.Parse(Sample);
            BlueprintFile copy = BlueprintFile.Parse(original.ToLines());
            Assert.Equal(original.Name, copy.Name);
            Assert.Equal(original.Description, copy.Description);
            Assert.Equal(original.Pieces.Count, copy.Pieces.Count);
            for (int i = 0; i < original.Pieces.Count; i++)
            {
                Assert.Equal(original.Pieces[i].Name, copy.Pieces[i].Name);
                Assert.Equal(original.Pieces[i].Position.X, copy.Pieces[i].Position.X, 4);
                Assert.Equal(original.Pieces[i].Rotation.W, copy.Pieces[i].Rotation.W, 4);
            }
        }
    }
}
