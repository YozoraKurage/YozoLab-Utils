using System.Collections.Generic;
using System.Linq;
using System.Text;
using JetBrains.Annotations;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEngine;
using YozoLab.SPS.Builder;
using YozoLab.SPS.Injector;
using YozoLab.SPS.Utils;
using YozoLab.SPS.Utils.Controller;

namespace YozoLab.SPS.Ndmf {
    /**
     * アバターに元からあるクリップ（MA Merge Animator で後から合流するものも含む）を、
     * SPS の処理から VFClip として触れるようにする。
     *
     * SPS が既存のクリップに手を入れるのは次の 2 つだけ。
     * - Plug のレンダラーを作り直したとき、そのレンダラーを動かすカーブを向け直す
     * - Plug / Socket が有効かどうかを知るため、表示切り替えのクリップに AAP のカーブを足す
     *
     * VRCFury はアバターのコントローラーを丸ごと自前のモデルに読み込んで書き戻していたが、
     * その往復で遷移やレイヤーを「修正」してしまう。ここでは各クリップを一時的な VFClip に写して
     * 渡し、最後に変わったカーブ（追加・変更・削除）だけを NDMF の仮想クリップへ書き戻す。
     * 触られなかったカーブ、パスが解決できないカーブには一切手を付けない。
     */
    [VFService]
    internal class ExistingClipsService {
        [VFAutowired] private readonly VFGameObject avatarObject;
        [VFAutowired] private readonly VRCFObjectPathCache objectPaths;
        [VFAutowired] [CanBeNull] private readonly NdmfSpsContext ndmf;

        private class Entry {
            public VirtualClip virtualClip;
            public VFClip clip;
            public Dictionary<EditorCurveBinding, string> snapshot;
        }

        private List<Entry> entries;

        public IList<VFClip> GetClips() {
            if (ndmf == null) return new VFClip[] { };
            if (entries == null) entries = ndmf.GetAllVirtualClips().Select(Load).ToList();
            return entries.Select(e => e.clip).ToList();
        }

        private Entry Load(VirtualClip virtualClip) {
            var context = new VFLoadContext {
                OwnerObject = avatarObject,
                AnimatorObject = avatarObject,
                RootBindingsApplyToAvatar = true,
                ObjectPaths = objectPaths,
                ReverseObjectPaths = true
            };
            var clip = VFClip.Create(virtualClip.Name);
            foreach (var binding in virtualClip.GetFloatCurveBindings()) {
                var curve = virtualClip.GetFloatCurve(binding);
                if (curve == null) continue;
                clip.SetCurve(ToVfBinding(binding, context), curve);
            }
            foreach (var binding in virtualClip.GetObjectCurveBindings()) {
                var curve = virtualClip.GetObjectCurve(binding);
                if (curve == null) continue;
                clip.SetCurve(ToVfBinding(binding, context), curve);
            }
            return new Entry {
                virtualClip = virtualClip,
                clip = clip,
                snapshot = Snapshot(clip)
            };
        }

        private static VFBinding ToVfBinding(EditorCurveBinding binding, VFLoadContext context) {
            if (VFBinding.IsAnimatorBinding(binding)) return VFBinding.MakeAnimatorBinding(binding.propertyName);
            return VFBinding.From(VFResolvedObject.Load(binding.path, context, binding.type), binding);
        }

        private Dictionary<EditorCurveBinding, string> Snapshot(VFClip clip) {
            var output = new Dictionary<EditorCurveBinding, string>();
            foreach (var (binding, curve) in clip.GetAllCurves()) {
                output[binding.ToEditorCurveBinding(avatarObject)] = Describe(curve);
            }
            return output;
        }

        /** 変わったカーブだけを書き戻す。SPS の処理がすべて終わってから一度だけ呼ぶ。 */
        public void Commit() {
            if (entries == null || ndmf == null) return;
            foreach (var entry in entries) {
                var after = new Dictionary<EditorCurveBinding, (VFBinding binding, FloatOrObjectCurve curve)>();
                foreach (var (binding, curve) in entry.clip.GetAllCurves()) {
                    after[binding.ToEditorCurveBinding(avatarObject)] = (binding, curve);
                }

                foreach (var removed in entry.snapshot.Keys.Where(k => !after.ContainsKey(k)).ToList()) {
                    if (IsObjectBinding(entry.virtualClip, removed)) {
                        entry.virtualClip.SetObjectCurve(removed, null);
                    } else {
                        entry.virtualClip.SetFloatCurve(removed, null);
                    }
                }

                foreach (var pair in after) {
                    var description = Describe(pair.Value.curve);
                    if (entry.snapshot.TryGetValue(pair.Key, out var before) && before == description) continue;

                    // 新しく足したカーブの先は、このあと Modular Avatar に動かされるかもしれない。
                    var key = pair.Key;
                    var target = pair.Value.binding.target;
                    if (!entry.snapshot.ContainsKey(key) && target != null) {
                        key.path = ndmf.VirtualPathFor(target);
                    }

                    if (pair.Value.curve.IsFloat) {
                        entry.virtualClip.SetFloatCurve(key, pair.Value.curve.FloatCurve);
                    } else {
                        entry.virtualClip.SetObjectCurve(key, pair.Value.curve.ObjectCurve);
                    }
                }
            }
            entries = null;
        }

        private static bool IsObjectBinding(VirtualClip clip, EditorCurveBinding binding) {
            return clip.GetObjectCurveBindings().Any(b => b.Equals(binding));
        }

        private static string Describe(FloatOrObjectCurve curve) {
            var sb = new StringBuilder();
            if (curve.IsFloat) {
                var c = curve.FloatCurve;
                sb.Append('f');
                foreach (var k in c.keys) {
                    sb.Append(k.time).Append(',').Append(k.value).Append(',')
                        .Append(k.inTangent).Append(',').Append(k.outTangent).Append(',')
                        .Append(k.inWeight).Append(',').Append(k.outWeight).Append(',')
                        .Append((int)k.weightedMode).Append(';');
                }
                sb.Append((int)c.preWrapMode).Append((int)c.postWrapMode);
            } else {
                sb.Append('o');
                foreach (var k in curve.ObjectCurve) {
                    sb.Append(k.time).Append(',').Append(k.value != null ? k.value.GetInstanceID() : 0).Append(';');
                }
            }
            return sb.ToString();
        }
    }
}
