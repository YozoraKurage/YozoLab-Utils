#!/usr/bin/env bash
# VPM パッケージ (VRChat SDK / NDMF など) をテストプロジェクトへ入れる（vrc-get 経由）。
#
#   add-vpm.sh nadena.dev.ndmf              最新を入れる
#   add-vpm.sh com.vrchat.avatars@3.10.4    版を指定する
#   add-vpm.sh --remove com.vrchat.avatars  抜く
#   add-vpm.sh --list                       いま入っているものを見る
#
# posebaker の NDMF 連携のように、外部パッケージがある場合だけコンパイルされる
# コードは、入れた状態でも一度テストを通しておくこと。

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

readonly VRC_GET_VERSION=1.9.2
readonly VRC_GET="$HOME/.local/bin/vrc-get"

# 公式・curated 以外で使うリスティング。vrc-get は同じ URL を二重登録しない。
readonly EXTRA_REPOS=(
  "https://vpm.nadena.dev/vpm.json"
)

ensure_vrc_get() {
  [[ -x "$VRC_GET" ]] && return 0
  # イメージに焼いてあれば PATH 上にいる。無ければ取ってくる（再ビルド直後など）。
  if command -v vrc-get >/dev/null 2>&1; then return 0; fi
  info "vrc-get を取得中…"
  mkdir -p "$(dirname "$VRC_GET")"
  curl -sSL --fail --max-time 180 -o "$VRC_GET" \
    "https://github.com/vrc-get/vrc-get/releases/download/v${VRC_GET_VERSION}/x86_64-unknown-linux-musl-vrc-get" \
    || die "vrc-get を取得できなかった"
  chmod +x "$VRC_GET"
}

vrc() { "$(command -v vrc-get || echo "$VRC_GET")" "$@"; }

ensure_repos() {
  local url
  for url in "${EXTRA_REPOS[@]}"; do
    vrc repo add "$url" >/dev/null 2>&1 || true
  done
}

main() {
  restore_license
  have_license || { license_hint; exit 4; }
  [[ -f "$UNITY_PROJECT/Packages/manifest.json" ]] || "$SCRIPT_DIR/setup.sh"
  ensure_vrc_get

  case "${1:-}" in
    --list)
      ( cd "$UNITY_PROJECT" && vrc info project ) ;;
    --remove)
      [[ $# -ge 2 ]] || die "--remove にはパッケージ名が要る"
      ( cd "$UNITY_PROJECT" && vrc remove "$2" -y )
      warm_up ;;
    -h|--help|"")
      sed -n '2,12p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//' ;;
    *)
      ensure_repos
      ( cd "$UNITY_PROJECT" && vrc install "$@" -y )
      warm_up ;;
  esac
}

main "$@"
