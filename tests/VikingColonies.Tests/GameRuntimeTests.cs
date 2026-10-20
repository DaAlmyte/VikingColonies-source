using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    // Seen: the Longhouse never shared out its beds; in the game, Homes.Assign threw "Could not load file or assembly
    // 'System.ValueTuple'". The game's runtime has no tuples, so the rules mustn't use them (C# tuple syntax compiles to them).
    public class GameRuntimeTests
    {
        [Fact]
        public void The_rules_use_no_tuples_the_game_cannot_load()
        {
            using (FileStream file = File.OpenRead(typeof(Homes).Assembly.Location))
            using (var pe = new PEReader(file))
            {
                MetadataReader md = pe.GetMetadataReader();
                string[] tuples = md.TypeReferences.Select(h => md.GetString(md.GetTypeReference(h).Name))
                                    .Where(n => n.StartsWith("ValueTuple")).Distinct().ToArray();
                Assert.Empty(tuples);
            }
        }
    }
}
