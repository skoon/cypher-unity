using System.Linq;
using System.Threading.Tasks;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class SignBakeTests
    {
        static readonly SignBake Baked = SignBake.Run();

        [Test]
        public void TheBakeHoldsBothAtlasesAtTheirSizes()
        {
            Assert.AreEqual(NeonAtlas.Size * NeonAtlas.Size, Baked.SignAtlas.Length);
            Assert.AreEqual(BoardArt.Size * BoardArt.Size * 4, Baked.BoardAtlas.Length);
            Assert.AreEqual(NeonAtlas.Size, Baked.Layout.Size);
        }

        [Test]
        public void ItIsTheSameBytesAsDrawingThemDirectly()
        {
            Assert.IsTrue(Baked.SignAtlas.SequenceEqual(NeonAtlas.Render(NeonAtlas.Layout())));
            Assert.IsTrue(Baked.BoardAtlas.SequenceEqual(BoardArt.Render()));
        }

        [Test]
        public void ItUsesTheLayoutItIsGiven()
        {
            NeonAtlasLayout layout = NeonAtlas.Layout();
            Assert.AreSame(layout, SignBake.Run(layout).Layout);
        }

        [Test]
        public void ItTimesEachPart()
        {
            Assert.Greater(Baked.SignMilliseconds, 0);
            Assert.Greater(Baked.BoardMilliseconds, 0);
        }

        [Test]
        public void SeveralBakesAtOnceOnWorkersAgreeBecauseNothingIsSharedBetweenThem()
        {
            // the loader bakes on a worker beside the prop bake: no hidden shared state may change the pixels
            Task<SignBake>[] tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(() => SignBake.Run())).ToArray();
            Task.WaitAll(tasks);
            foreach (Task<SignBake> t in tasks)
            {
                Assert.IsTrue(t.Result.SignAtlas.SequenceEqual(Baked.SignAtlas));
                Assert.IsTrue(t.Result.BoardAtlas.SequenceEqual(Baked.BoardAtlas));
            }
        }
    }
}
