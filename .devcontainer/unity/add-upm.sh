#!/usr/bin/env bash
# 通常の UPM パッケージ（Unity レジストリのもの）をテストプロジェクトへ入れる。
# manifest.json を直接書き換えるだけなので、vrc-get は関わらない。
#
#   add-upm.sh com.unity.formats.fbx           最新の既知版を入れる
#   add-upm.sh com.unity.formats.fbx@4.2.1     版を指定する
#   add-upm.sh --remove com.unity.formats.fbx  抜く
#   add-upm.sh --list                          いま入っているものを見る
#
# FBX Animation Baker は Unity FBX Exporter (com.unity.formats.fbx) が無くても
# コンパイルは通る（リフレクション経由で呼ぶため）が、実際に書き出す動作を
# 確かめるには入っている必要がある。

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

readonly MANIFEST="$UNITY_PROJECT/Packages/manifest.json"

# 版を省略されたときに使う既定。Unity 2022.3 で動くことを確認している版に固定
# しておく（"latest" のような指定は manifest では使えない）。
default_version() {
  case "$1" in
    com.unity.formats.fbx) echo "4.2.1" ;;
    com.unity.timeline)    echo "1.7.6" ;;
    *) return 1 ;;
  esac
}

edit_manifest() {
  local tmp="$MANIFEST.tmp"
  jq "$@" "$MANIFEST" > "$tmp" && mv "$tmp" "$MANIFEST"
}

cmd_add() {
  local spec="$1" name version
  name="${spec%@*}"
  if [[ "$spec" == *@* ]]; then
    version="${spec##*@}"
  else
    version="$(default_version "$name")" \
      || die "$name の既定版を知らない。add-upm.sh $name@<version> のように版を指定すること"
  fi

  edit_manifest --arg n "$name" --arg v "$version" '.dependencies[$n] = $v'
  info "$name@$version を manifest に追加した"
  warm_up
}

cmd_remove() {
  edit_manifest --arg n "$1" 'del(.dependencies[$n])'
  info "$1 を manifest から削除した"
  warm_up
}

main() {
  restore_license
  have_license || { license_hint; exit 4; }
  [[ -f "$MANIFEST" ]] || "$SCRIPT_DIR/setup.sh"

  case "${1:-}" in
    --list)     jq -r '.dependencies | to_entries[] | "\(.key)  \(.value)"' "$MANIFEST" ;;
    --remove)   [[ $# -ge 2 ]] || die "--remove にはパッケージ名が要る"; cmd_remove "$2" ;;
    -h|--help|"") sed -n '2,12p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//' ;;
    *)          cmd_add "$1" ;;
  esac
}

main "$@"
