#!/usr/bin/env python3
"""
VRCFury のソースから SPS 部分を取り出し、net.yozolab.sps として付け替える機械的な変換。

  port.py VRCFURY_PACKAGE_DIR OUT_DIR

やること（ここでは機能の取捨はしない。機械的に名前だけ変える）:
- Runtime / Editor-Common / Editor-Avatars / SPS / VrcfResources を写す
- 名前空間 VF → YozoLab.SPS
- アセンブリ名と InternalsVisibleTo
- ランタイムコンポーネントのクラス名・ファイル名（VRCFuryHapticPlug → SpsPlug など）
- すべての .meta の GUID を振り直し、テキスト中の旧 GUID を置き換える
  （VRCFury と同じプロジェクトに入っても衝突しないように）
- 一時パッケージのパス、隠しシェーダー名、SPS で加工したシェーダー名

他のアバターの SPS と反応し合うための取り決め（接触判定のタグ、ライトの値、OSC の
パラメータ名など）は一切変えない。
"""
import os, re, shutil, sys, uuid

SRC, OUT = sys.argv[1], sys.argv[2]

KEEP_DIRS = ["Runtime", "Editor-Common", "Editor-Avatars", "SPS", "VrcfResources"]

ASM = {
    "VRCFury": "YozoLab.SPS.Runtime",
    "VRCFury-Editor-Common": "YozoLab.SPS.Editor.Common",
    "VRCFury-Editor-Avatars": "YozoLab.SPS.Editor",
}

# ユーザーの目に触れるランタイムコンポーネント（と、ビルド時にアバターへ付ける補助コンポーネント）。
# MonoBehaviour はクラス名とファイル名が一致している必要があるので、両方変える。
COMPONENTS = {
    "VRCFuryHapticPlug": "SpsPlug",
    "VRCFuryHapticSocket": "SpsSocket",
    "VRCFuryHapticTouchReceiver": "SpsTouchReceiver",
    "VRCFuryHapticTouchSender": "SpsTouchSender",
    "VRCFurySocketGizmo": "SpsSocketGizmo",
    "VRCFurySpsGreenScreenFix": "SpsGreenScreenFix",
    "VRCFuryGlobalCollider": "SpsGlobalCollider",
    "VRCFuryHideGizmoUnlessSelected": "SpsHideGizmoUnlessSelected",
    "VRCFuryNoUpdateWhenOffscreen": "SpsNoUpdateWhenOffscreen",
    "VRCFuryPlayComponent": "SpsPlayComponent",
    "VRCFuryComponent": "SpsComponent",
    # 編集用のクラスも名前を合わせる（ファイル名が型名と対になっているもの）
    "VRCFuryHapticPlugEditor": "SpsPlugEditor",
    "VRCFuryHapticSocketEditor": "SpsSocketEditor",
    "VRCFuryHapticPlugBaker": "SpsPlugBaker",
    "VRCFuryHapticSocketBaker": "SpsSocketBaker",
    "VRCFuryHapticSocketGizmo": "SpsSocketGizmoDrawer",
    "VRCFuryHapticTouchReceiverBuilder": "SpsTouchReceiverBuilder",
    "VRCFuryHapticTouchSenderBuilder": "SpsTouchSenderBuilder",
    "VRCFurySpsGreenScreenFixEditor": "SpsGreenScreenFixEditor",
}

TEXT_EXT = {".cs", ".asmdef", ".asmref", ".json", ".shader", ".cginc", ".hlsl", ".uss", ".uxml",
            ".asset", ".prefab", ".mat", ".controller", ".anim", ".meta", ".rsp", ".md", ".txt"}


def copy_tree():
    if os.path.exists(OUT):
        shutil.rmtree(OUT)
    os.makedirs(OUT)
    for d in KEEP_DIRS:
        shutil.copytree(os.path.join(SRC, d), os.path.join(OUT, d))
        if os.path.exists(os.path.join(SRC, d + ".meta")):
            shutil.copy(os.path.join(SRC, d + ".meta"), os.path.join(OUT, d + ".meta"))


def all_files():
    for root, _, files in os.walk(OUT):
        for f in files:
            yield os.path.join(root, f)


def rename_component_files():
    for path in list(all_files()):
        base = os.path.basename(path)
        for old, new in COMPONENTS.items():
            for ext in (".cs", ".cs.meta"):
                if base == old + ext:
                    os.rename(path, os.path.join(os.path.dirname(path), new + ext))


def remap_guids():
    mapping = {}
    for path in all_files():
        if not path.endswith(".meta"):
            continue
        text = open(path, encoding="utf-8", errors="ignore").read()
        m = re.search(r"^guid:\s*([0-9a-f]{32})", text, re.M)
        if m:
            mapping[m.group(1)] = uuid.uuid4().hex
    pattern = re.compile("|".join(mapping)) if mapping else None
    for path in all_files():
        if os.path.splitext(path)[1] not in TEXT_EXT and not path.endswith(".meta"):
            continue
        text = open(path, encoding="utf-8", errors="ignore").read()
        new = pattern.sub(lambda m: mapping[m.group(0)], text) if pattern else text
        if new != text:
            open(path, "w", encoding="utf-8").write(new)
    return mapping


def transform_code(text):
    # 名前空間。VF 単独のトークンか、VF. で始まる修飾名だけを置き換える（VFGameObject などは対象外）。
    text = re.sub(r"\bnamespace VF\b", "namespace YozoLab.SPS", text)
    text = re.sub(r"\busing VF\b", "using YozoLab.SPS", text)
    text = re.sub(r"(?<![\w.])VF\.(?=[A-Z])", "YozoLab.SPS.", text)
    text = re.sub(r"(using\s+\w+\s*=\s*)VF\.", r"\1YozoLab.SPS.", text)

    for old, new in COMPONENTS.items():
        text = re.sub(r"\b%s\b" % old, new, text)

    for old, new in ASM.items():
        text = text.replace('InternalsVisibleTo("%s")' % old, 'InternalsVisibleTo("%s")' % new)

    # VRCFury 本体と取り合わないもの
    text = text.replace("Packages/com.vrcfury.temp", "Packages/net.yozolab.sps.temp")
    text = text.replace("com.vrcfury.temp", "net.yozolab.sps.temp")
    text = text.replace("Hidden/VRCFury/", "Hidden/YozoLab/SPS/")
    text = text.replace("Hidden/Locked/SPSPatched/", "Hidden/Locked/YozoLabSPSPatched/")
    text = text.replace("Hidden/SPSPatched/", "Hidden/YozoLabSPSPatched/")
    return text


def transform_asmdef(text):
    for old, new in ASM.items():
        text = re.sub(r'"%s"' % re.escape(old), '"%s"' % new, text)
    text = text.replace('"rootNamespace": "VF"', '"rootNamespace": "YozoLab.SPS"')
    return text


def transform():
    for path in list(all_files()):
        ext = os.path.splitext(path)[1]
        if ext == ".cs":
            t = open(path, encoding="utf-8-sig").read()
            open(path, "w", encoding="utf-8").write(transform_code(t))
        elif ext in (".shader", ".cginc", ".hlsl"):
            t = open(path, encoding="utf-8", errors="ignore").read()
            n = t.replace("Hidden/VRCFury/", "Hidden/YozoLab/SPS/")
            if n != t:
                open(path, "w", encoding="utf-8").write(n)
        elif ext == ".asmdef":
            t = open(path, encoding="utf-8-sig").read()
            newpath = path
            for old, new in ASM.items():
                if os.path.basename(path) == old + ".asmdef":
                    newpath = os.path.join(os.path.dirname(path), new + ".asmdef")
            open(newpath, "w", encoding="utf-8").write(transform_asmdef(t))
            if newpath != path:
                os.remove(path)
                os.rename(path + ".meta", newpath + ".meta")


if __name__ == "__main__":
    copy_tree()
    rename_component_files()
    transform()
    mapping = remap_guids()
    print("files:", sum(1 for _ in all_files()), "guids remapped:", len(mapping))
