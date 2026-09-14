#!/usr/bin/env bash
# エディタを実際に描画させて、その画面を PNG に撮る。
#
#   run-capture.sh out.png                 テーマ有効で撮る
#   run-capture.sh out.png --debug-paint   仕組みごとの原色で撮る
#   run-capture.sh out.png --no-theme      テーマ無効で撮る
#   run-capture.sh out.png --trace         何がどこを塗ったかもログに出す
#   run-capture.sh out.png --select        オブジェクトを選んで Inspector に中身を出す
#   run-capture.sh out.png --windows AnimationWindow,ConsoleWindow  指定のウィンドウも開く
#
# batchmode では窓が無く、何も描かれない。ここでは Xvfb の仮想ディスプレイに
# GUI の Unity を立ち上げ、画面が落ち着いた頃に Unity 自身の ReadScreenPixel で
# 撮らせて PNG に書かせる。スクショ用の外部コマンドがコンテナに無いため。
#
# 撮影スクリプトは ScreenCapture.cs.txt。テストプロジェクトの Assets/Editor へ置く。

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/common.sh"

OUT="${1:?出力する PNG のパスが要る}"; shift || true
THEME=1; DEBUG_PAINT=0; TRACE=0; SELECT=0; WINDOWS=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --debug-paint) DEBUG_PAINT=1; shift ;;
    --no-theme)    THEME=0; shift ;;
    --trace)       TRACE=1; shift ;;
    --select)      SELECT=1; shift ;;
    --windows)     WINDOWS="${2:?--windows に型名が要る}"; shift 2 ;;
    *) die "不明な引数: $1" ;;
  esac
done

restore_license
have_license || { license_hint; exit 4; }

readonly DISPLAY_NUM="${YOZOLAB_CAPTURE_DISPLAY:-:99}"
readonly SCREEN="${YOZOLAB_CAPTURE_SCREEN:-2560x1440x24}"

# xdpyinfo が無い環境なので、X のソケットの有無で判定する。
if [[ ! -S "/tmp/.X11-unix/X${DISPLAY_NUM#:}" ]]; then
  info "仮想ディスプレイ $DISPLAY_NUM を起動する"
  Xvfb "$DISPLAY_NUM" -screen 0 "$SCREEN" -nolisten tcp &
  # X のソケットが出来るまで待つ
  for _ in $(seq 1 100); do
    [[ -S "/tmp/.X11-unix/X${DISPLAY_NUM#:}" ]] && break
    python3 -c 'import time; time.sleep(0.1)'
  done
fi

# 撮影スクリプトをテストプロジェクトへ置く
mkdir -p "$UNITY_PROJECT/Assets/Editor"
cp "$SCRIPT_DIR/ScreenCapture.cs.txt" "$UNITY_PROJECT/Assets/Editor/YozoLabScreenCapture.cs"

mkdir -p "$UNITY_LOG_DIR"
readonly LOG="$UNITY_LOG_DIR/capture.log"
rm -f "$OUT" "$LOG"

info "撮影中… (ログ: $LOG)"
DISPLAY="$DISPLAY_NUM" \
YOZOLAB_CAPTURE="$OUT" \
YOZOLAB_CAPTURE_THEME="$THEME" \
YOZOLAB_CAPTURE_DEBUGPAINT="$DEBUG_PAINT" \
YOZOLAB_TRACE_PAINT="$TRACE" \
YOZOLAB_CAPTURE_SELECT="$SELECT" \
YOZOLAB_CAPTURE_WINDOWS="$WINDOWS" \
  "$UNITY_BIN" -projectPath "$UNITY_PROJECT" -logFile "$LOG" || true

if [[ -s "$OUT" ]]; then
  info "撮れた: $OUT ($(stat -c%s "$OUT") bytes)"
else
  warn "撮れなかった。ログを見ること: $LOG"
  grep -a "error CS\|\[CAPTURE\]" "$LOG" | head -10 || true
  exit 1
fi

[[ "$TRACE" == "1" ]] && { info "描画の記録:"; grep -a "\[TRACE\]" "$LOG" | head -40; }
exit 0
