using System.Runtime.CompilerServices;

// 半径の逆算やハンドルの向き決めは、シーン上でしか確かめられないと直しづらい。
// 純粋な計算だけを切り出してあるので、テストから直接叩けるようにしておく。
[assembly: InternalsVisibleTo("net.yozolab.yozolab-utils.Tests")]
