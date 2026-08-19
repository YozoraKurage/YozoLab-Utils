#!/usr/bin/env bash
# テスト用 Unity プロジェクトを用意する。postCreateCommand から呼ばれるほか、
# 手で何度実行しても同じ状態になる（冪等）。
#
# プロジェクト本体はボリューム側 ($YOZOLAB_UNITY_PROJECT) に置き、このリポジトリは
# ローカルパッケージ (file:/workspace) として参照させる。リポジトリ側には
# Library/ も Assets/ も作らない。

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

editor_version() {
  if [[ -r "${UNITY_PATH:-/opt/unity}/version" ]]; then
    cat "${UNITY_PATH:-/opt/unity}/version"
  else
    echo "${UNITY_VERSION:-2022.3.22f1}"
  fi
}

# manifest は初回だけテンプレートから作る。2 回目以降は add-vpm.sh / add-upm.sh で
# 足したパッケージを消さないよう、このリポジトリの参照と testables だけを保証する。
sync_manifest() {
  local manifest="$UNITY_PROJECT/Packages/manifest.json"

  if [[ ! -f "$manifest" ]]; then
    cp "$SCRIPT_DIR/manifest.json" "$manifest"
    return
  fi

  local tmp="$manifest.tmp"
  jq --arg pkg "$PACKAGE_NAME" \
     '.dependencies[$pkg] = "file:/workspace"
      | .testables = ((.testables // []) + [$pkg] | unique)' \
     "$manifest" > "$tmp" && mv "$tmp" "$manifest"
}

scaffold_project() {
  local version
  version="$(editor_version)"

  mkdir -p "$UNITY_PROJECT/Assets" "$UNITY_PROJECT/Packages" \
           "$UNITY_PROJECT/ProjectSettings" "$UNITY_LOG_DIR"

  sync_manifest

  # これが無いと Unity が「別バージョンで作られたプロジェクト」とみなす。
  printf 'm_EditorVersion: %s\n' "$version" > "$UNITY_PROJECT/ProjectSettings/ProjectVersion.txt"

  info "テストプロジェクト: $UNITY_PROJECT (Unity $version)"
}

check_drop_dir() {
  # temp/ (チルダ無し) はローカルパッケージの一部として Unity にインポートされて
  # しまう。ユーザー提供の生成 cs や FBX が入っていると丸ごと取り込まれるので、
  # 置き場は temp~/ に限る。
  if [[ -d "$PACKAGE_ROOT/temp" ]]; then
    warn "$PACKAGE_ROOT/temp が存在する。Unity のインポート対象に入ってしまうので"
    warn "temp~ にリネームすること:  mv /workspace/temp /workspace/temp~"
  fi
}

check_optional_deps() {
  # posebaker は NDMF を versionDefines 経由で使う（無くてもコードは
  # #if YOZOLAB_NDMF で切られる）が、asmdef の references には常に載っている。
  # そのため NDMF が無いと「存在しないアセンブリを参照している」というエラーが
  # 出て net.yozolab.posebaker.Editor だけコンパイルされない。
  # posebaker を触るときは入れること。
  if [[ ! -d "$UNITY_PROJECT/Packages/nadena.dev.ndmf" ]] \
     && ! jq -e '.dependencies["nadena.dev.ndmf"]' "$UNITY_PROJECT/Packages/manifest.json" >/dev/null 2>&1; then
    info "NDMF は未導入（posebaker のみ影響）。要るときは: add-vpm.sh nadena.dev.ndmf"
  fi

  # FBX Animation Baker は Unity FBX Exporter をリフレクション経由で呼ぶので、
  # 無くてもコンパイルは通る。実際に書き出すテストをするなら入れること。
  if ! jq -e '.dependencies["com.unity.formats.fbx"]' "$UNITY_PROJECT/Packages/manifest.json" >/dev/null 2>&1; then
    info "Unity FBX Exporter は未導入。要るときは: add-upm.sh com.unity.formats.fbx"
  fi
}

check_editor() {
  # ベースイメージの Unity は root がインストールしたもの。node から実行・読み取り
  # できるかをここで一度だけ確かめておく（駄目なら症状がテスト実行時の不可解な
  # エラーとして出るので、先に潰す）。
  [[ -x "$UNITY_BIN" ]] || die "Unity 本体が実行できない: $UNITY_BIN"
  [[ -r "${UNITY_PATH:-/opt/unity}/Editor/Data/Managed/UnityEngine.dll" ]] \
    || warn "Unity の Data ディレクトリが読めないかもしれない（権限を確認すること）"
}

main() {
  check_editor
  restore_license
  scaffold_project
  check_drop_dir
  check_optional_deps

  if ! have_license; then
    license_hint
    info "ライセンス有効化後に .devcontainer/unity/setup.sh をもう一度実行すること"
    return 0
  fi

  info "パッケージ解決と初回インポートを実行中（数分かかる）"
  warm_up
}

main "$@"
