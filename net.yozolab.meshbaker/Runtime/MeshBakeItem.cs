using System.Collections.Generic;
using UnityEngine;

namespace YozoLab.MeshBaker
{
    /// <summary>
    /// ベイク出力1グループ分のRendererを管理するコンポーネント。
    /// MeshBakeGroupの子に置くと出力グループとして解決され、
    /// グループごとに別の静的Mesh/プレハブが出力される
    /// （MeshBakeGroup側の配列ベースのRenderer Groupsとも併用できる）。
    ///
    /// Rendererは明示リストと、このGameObject配下からの自動収集の両方で集められる。
    /// グループ名（未設定ならGameObject名）が成果物アセット名のサフィックスになる。
    /// </summary>
    [AddComponentMenu("YozoLab/Mesh Bake Item")]
    [DisallowMultipleComponent]
    public class MeshBakeItem : MonoBehaviour
    {
        [Tooltip("アイテム名。空ならこのGameObjectの名前を使います（成果物アセット名のサフィックス）。")]
        public string itemName = "";

        [Tooltip("このアイテムとしてベイクするRenderer（明示指定。階層外のRendererも指定可能）。")]
        public List<Renderer> renderers = new List<Renderer>();

        [Tooltip("このGameObject配下のRendererを自動的にこのアイテムへ含めます。" +
                 "より深い階層に別のMesh Bake Itemがある場合は、近い方のアイテムが優先されます。")]
        public bool includeChildRenderers = true;

        public string EffectiveName => string.IsNullOrEmpty(itemName) ? gameObject.name : itemName;
    }
}
