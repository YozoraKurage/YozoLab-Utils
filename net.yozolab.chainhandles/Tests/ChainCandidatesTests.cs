using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using YozoLab.ChainHandles;

namespace YozoLab.Tests
{
    /// <summary>
    /// 右クリックから出す鎖の候補が、選んだ場所に応じて妥当に並ぶか。
    /// 木の形だけの問題なので、GameObject ではなく偽のノードで組む。
    /// </summary>
    public class ChainCandidatesTests
    {
        private sealed class Node
        {
            public readonly string name;
            public Node parent;
            public readonly List<Node> children = new List<Node>();
            public bool usable = true;

            public Node(string name, Node parent = null)
            {
                this.name = name;
                this.parent = parent;
                parent?.children.Add(this);
            }

            public override string ToString() => name;
        }

        private static List<string> Find(Node selected) =>
            ChainCandidates.Find(selected, n => n.parent, n => n.children, n => n.usable)
                .Select(c => $"{c.start.name}>{c.end.name}")
                .ToList();

        /// <summary>
        /// Head の下に髪の房が 2 本と、枝分かれする房が 1 本。
        /// Head ─┬ A1 ─ A2 ─ A3
        ///       ├ B1 ─ B2
        ///       └ C1 ─ C2 ─┬ C3a
        ///                  └ C3b
        /// </summary>
        private static Dictionary<string, Node> MakeHead()
        {
            var nodes = new Dictionary<string, Node>();
            Node Add(string name, string parent) =>
                nodes[name] = new Node(name, parent == null ? null : nodes[parent]);

            Add("Head", null);
            Add("A1", "Head"); Add("A2", "A1"); Add("A3", "A2");
            Add("B1", "Head"); Add("B2", "B1");
            Add("C1", "Head"); Add("C2", "C1"); Add("C3a", "C2"); Add("C3b", "C2");
            return nodes;
        }

        [Test]
        public void StrandRoot_OffersItsTip()
        {
            Dictionary<string, Node> n = MakeHead();
            CollectionAssert.AreEqual(new[] { "A1>A3" }, Find(n["A1"]));
        }

        [Test]
        public void MiddleOfStrand_ClimbsToRootFirst()
        {
            // 房の途中を選んでも、まず房の根元から毛先まで。次いで根元から選んだ所まで、
            // 選んだ所から毛先まで。Head は子が複数なので越えない。
            Dictionary<string, Node> n = MakeHead();
            CollectionAssert.AreEqual(new[] { "A1>A3", "A1>A2", "A2>A3" }, Find(n["A2"]));
        }

        [Test]
        public void Tip_ClimbsToRoot()
        {
            Dictionary<string, Node> n = MakeHead();
            CollectionAssert.AreEqual(new[] { "A1>A3" }, Find(n["A3"]));
        }

        [Test]
        public void BranchingStrand_OffersBranchPointAndEachTip()
        {
            Dictionary<string, Node> n = MakeHead();
            CollectionAssert.AreEqual(new[] { "C1>C2", "C1>C3a", "C1>C3b" }, Find(n["C1"]));
        }

        [Test]
        public void HeadItself_OffersEveryStrand()
        {
            Dictionary<string, Node> n = MakeHead();
            CollectionAssert.AreEqual(
                new[] { "Head>A3", "Head>B2", "Head>C2", "Head>C3a", "Head>C3b" },
                Find(n["Head"]));
        }

        [Test]
        public void UnusableNodes_AreSkippedAndNotClimbedOver()
        {
            // メッシュ（usable = false）の兄弟は数えないので、Root の子は実質 Bone1 だけ。
            // ただし Root 自体が使えない（アバターのルート相当）なら、そこで止まる。
            var root = new Node("Root") { usable = false };
            var bone1 = new Node("Bone1", root);
            var bone2 = new Node("Bone2", bone1);
            var mesh = new Node("Mesh", bone1) { usable = false };
            new Node("Bone3", bone2);

            CollectionAssert.AreEqual(new[] { "Bone1>Bone3" }, Find(bone2).Take(1).ToList());
            CollectionAssert.DoesNotContain(Find(bone1), "Bone1>Mesh");
            Assert.IsEmpty(Find(mesh));
        }

        [Test]
        public void LeafWithNoLinearParent_HasNoCandidates()
        {
            Dictionary<string, Node> n = MakeHead();
            var lone = new Node("Lone", n["Head"]);
            Assert.IsEmpty(Find(lone));
        }

        // ---- 複数選択を一本の並びにする ----------------------------------

        private static List<string> Order(params Node[] nodes) =>
            ChainCandidates.TryOrderOnLine(nodes, n => n.parent, out List<Node> ordered)
                ? ordered.Select(n => n.name).ToList()
                : null;

        [Test]
        public void SelectionOnOneLine_IsOrderedFromRoot()
        {
            // 選んだ順はばらばらでも、根元から順に並ぶ。間の飛ばした関節は問わない。
            Dictionary<string, Node> n = MakeHead();
            CollectionAssert.AreEqual(new[] { "Head", "A1", "A3" }, Order(n["A3"], n["Head"], n["A1"]));
            CollectionAssert.AreEqual(new[] { "C1", "C2", "C3b" }, Order(n["C3b"], n["C1"], n["C2"]));
        }

        [Test]
        public void SelectionAcrossBranches_IsRejected()
        {
            Dictionary<string, Node> n = MakeHead();
            Assert.IsNull(Order(n["A1"], n["A2"], n["B2"]));      // 別の房
            Assert.IsNull(Order(n["C1"], n["C3a"], n["C3b"]));    // 枝分かれの先どうし
        }

        [Test]
        public void DuplicateSelection_IsIgnored()
        {
            Dictionary<string, Node> n = MakeHead();
            CollectionAssert.AreEqual(new[] { "A1", "A3" }, Order(n["A1"], n["A3"], n["A1"]));
        }

        [Test]
        public void ManyBranches_AreCapped()
        {
            var root = new Node("Root");
            for (int i = 0; i < 200; i++) new Node($"L{i}", root);
            Assert.AreEqual(ChainCandidates.MaxCandidates, Find(root).Count);
        }
    }
}
