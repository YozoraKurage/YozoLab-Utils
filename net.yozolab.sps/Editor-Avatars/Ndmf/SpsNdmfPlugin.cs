using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using YozoLab.SPS.Ndmf;
using YozoLab.SPS.Preview;

[assembly: ExportsPlugin(typeof(SpsNdmfPlugin))]

namespace YozoLab.SPS.Ndmf {
    /**
     * YozoLab SPS の NDMF プラグイン。
     *
     * Generating フェーズで、Modular Avatar より前に動く。
     * - MA が生成物（SPS のパラメータとメニューの MA コンポーネント）を処理できるように、MA より前。
     * - MA Merge Animator のコントローラーも含めて既存のアニメーションを見られるように、
     *   NDMF のアニメーター管理（AnimatorServicesContext）を有効にした状態で動かす。
     *   このあと MA が同じ仮想コントローラーの上で合流を行うので、SPS が足したレイヤーや
     *   既存クリップへの手直しはそのまま引き継がれる。
     * プレイモードでの確認も、NDMF の Apply on Play でそのまま動く。
     * エディタで止まったままの変形は、NDMF のプレビュー（SpsPreviewFilter）で見られる。
     */
    internal class SpsNdmfPlugin : Plugin<SpsNdmfPlugin> {
        public override string QualifiedName => "net.yozolab.sps";
        public override string DisplayName => "YozoLab SPS";

        protected override void Configure() {
            InPhase(BuildPhase.Generating)
                .BeforePlugin("nadena.dev.modular-avatar")
                .WithRequiredExtension(typeof(AnimatorServicesContext), seq => {
                    seq.Run("Build SPS", SpsBuilder.Run)
                        .PreviewingWith(new SpsPreviewFilter());
                });
        }
    }
}
