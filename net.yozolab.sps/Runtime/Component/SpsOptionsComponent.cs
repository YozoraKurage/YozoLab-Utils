using UnityEngine;
using YozoLab.SPS.Model.Feature;

namespace YozoLab.SPS.Component {
    /**
     * SPS のメニューの置き場所やアイコンなど、アバター全体の SPS の設定。
     * （VRCFury では VRCFury コンポーネントの中の「SPS Options」機能だったもの。）
     * アバターに一つだけ置ける。
     */
    [AddComponentMenu("YozoLab/SPS/SPS Options")]
    [DisallowMultipleComponent]
    internal class SpsOptionsComponent : SpsComponent {
        public SpsOptions options = new SpsOptions();
    }
}
