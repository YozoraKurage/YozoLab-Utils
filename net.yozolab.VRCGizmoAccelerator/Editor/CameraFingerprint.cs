using UnityEngine;

namespace YozoLab.VRCGizmoAccelerator
{
    /// <summary>
    /// 描画中のカメラの「見え方」を 1 つの整数にまとめる。
    ///
    /// コライダーのギズモは HandleUtility.GetHandleSize を使う（画面上で一定の
    /// 大きさに見せるため）。この手の値はカメラが動くと変わるので、キャッシュを
    /// そのまま使うと古い大きさのまま残る。
    ///
    /// かといってキャッシュを諦めると毎フレーム作り直しになる（実測 9ms）。
    /// カメラも指紋に含めておけば、カメラが止まっている間はキャッシュが効き、
    /// 動かした瞬間に作り直される。
    /// </summary>
    internal static class CameraFingerprint
    {
        internal static int Compute()
        {
            var camera = Camera.current;
            if (camera == null) return 0;

            unchecked
            {
                var m = camera.transform.localToWorldMatrix;

                int hash = 17;
                hash = hash * 31 + m.m00.GetHashCode();
                hash = hash * 31 + m.m01.GetHashCode();
                hash = hash * 31 + m.m02.GetHashCode();
                hash = hash * 31 + m.m03.GetHashCode();
                hash = hash * 31 + m.m10.GetHashCode();
                hash = hash * 31 + m.m11.GetHashCode();
                hash = hash * 31 + m.m12.GetHashCode();
                hash = hash * 31 + m.m13.GetHashCode();
                hash = hash * 31 + m.m20.GetHashCode();
                hash = hash * 31 + m.m21.GetHashCode();
                hash = hash * 31 + m.m22.GetHashCode();
                hash = hash * 31 + m.m23.GetHashCode();

                // 画角（ズーム）でも見た目の大きさが変わる
                hash = hash * 31 + camera.fieldOfView.GetHashCode();
                hash = hash * 31 + camera.orthographicSize.GetHashCode();
                hash = hash * 31 + (camera.orthographic ? 1 : 0);
                hash = hash * 31 + camera.pixelHeight;

                return hash;
            }
        }
    }
}
