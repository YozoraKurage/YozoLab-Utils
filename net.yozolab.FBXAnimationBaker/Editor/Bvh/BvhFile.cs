using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace YozoLab.FBXAnimationBaker.Bvh
{
    /// <summary>
    /// BVH のチャンネル 1 本。MOTION 行の 1 列に対応する。
    /// </summary>
    public enum BvhChannel
    {
        PositionX,
        PositionY,
        PositionZ,
        RotationX,
        RotationY,
        RotationZ,
    }

    /// <summary>
    /// BVH の HIERARCHY に出てくるジョイント 1 個。
    ///
    /// End Site は子を持たない終端で、OFFSET しか持たない。骨の長さを表すためだけに
    /// あるので <see cref="IsEndSite"/> で区別する。Humanoid の Avatar を組むときは
    /// 末端の向きを決める材料になるため、捨てずに階層へ残す。
    /// </summary>
    public sealed class BvhJoint
    {
        public string Name;
        public BvhJoint Parent;
        public readonly List<BvhJoint> Children = new List<BvhJoint>();

        /// <summary>親からの相対位置。BVH のファイル単位（多くは cm）。</summary>
        public Vector3 Offset;

        /// <summary>MOTION 行のどの列を読むか。宣言順がそのまま回転の適用順になる。</summary>
        public readonly List<BvhChannel> Channels = new List<BvhChannel>();

        /// <summary>MOTION 行の中で、このジョイントのチャンネルが始まる列。</summary>
        public int ChannelStart;

        public bool IsEndSite;

        /// <summary>ルートからのパス。Unity の Transform パスと同じ区切り。</summary>
        public string Path
        {
            get
            {
                if (Parent == null) return Name;
                return Parent.Path + "/" + Name;
            }
        }
    }

    /// <summary>
    /// BVH がどの軸を上としているか。
    ///
    /// 規格は Y アップだが、実際には Z アップで書き出すツールが珍しくない。
    /// 取り違えると骨格が寝たまま組まれ、Humanoid Avatar を作れない。
    /// </summary>
    public enum BvhUpAxis
    {
        /// <summary>OFFSET の広がりから推測する。</summary>
        Auto = 0,
        Y = 1,
        Z = 2,
    }

    /// <summary>
    /// 読み込んだ BVH 1 ファイル分。
    ///
    /// パースだけを持ち、Unity のオブジェクトは作らない。座標系の変換もここではしない。
    /// 生の値のままにしておかないと、あとで軸の取り違えを追うときに
    /// 「ファイルがそう書いてあるのか、変換で曲がったのか」が分からなくなる。
    /// </summary>
    public sealed class BvhFile
    {
        public BvhJoint Root { get; private set; }

        /// <summary>End Site を含む全ジョイント。宣言順。</summary>
        public IReadOnlyList<BvhJoint> Joints => joints;

        /// <summary>MOTION の 1 行 = 1 フレーム。列数は <see cref="ChannelCount"/>。</summary>
        public IReadOnlyList<float[]> Frames => frames;

        /// <summary>1 フレームの秒数。BVH の "Frame Time"。</summary>
        public float FrameTime { get; private set; }

        public int ChannelCount { get; private set; }

        public float FrameRate => FrameTime > 0f ? 1f / FrameTime : 30f;

        /// <summary>
        /// OFFSET の広がりから上方向を当てる。
        ///
        /// 背骨と脚は上下に伸び、腕は左右に伸びる。つまり X 以外で最も伸びている軸が上。
        /// 腕は両軸に等しく寄与しないので、Y と Z の総和を比べれば足りる。
        /// </summary>
        public BvhUpAxis GuessUpAxis()
        {
            float y = 0f, z = 0f;
            foreach (BvhJoint joint in joints)
            {
                y += Mathf.Abs(joint.Offset.y);
                z += Mathf.Abs(joint.Offset.z);
            }
            return z > y ? BvhUpAxis.Z : BvhUpAxis.Y;
        }

        private readonly List<BvhJoint> joints = new List<BvhJoint>();
        private readonly List<float[]> frames = new List<float[]>();

        /// <summary>
        /// ファイルを読む。壊れていれば <see cref="BvhParseException"/> を投げる。
        /// </summary>
        public static BvhFile Load(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath))
            {
                throw new BvhParseException($"BVH ファイルが見つかりません: {absolutePath}");
            }

            return Parse(File.ReadAllText(absolutePath), Path.GetFileName(absolutePath));
        }

        public static BvhFile Parse(string text, string sourceName = "(text)")
        {
            var file = new BvhFile();
            var reader = new TokenReader(text, sourceName);

            reader.Expect("HIERARCHY");
            file.Root = file.ParseJoint(reader, null, isRoot: true);
            file.AssignChannelColumns();

            reader.Expect("MOTION");
            file.ParseMotion(reader);

            return file;
        }

        // ─────────────────────────────────────────────────────────────
        //  HIERARCHY
        // ─────────────────────────────────────────────────────────────

        private BvhJoint ParseJoint(TokenReader reader, BvhJoint parent, bool isRoot)
        {
            string keyword = reader.Next();
            bool endSite;

            if (isRoot)
            {
                if (!string.Equals(keyword, "ROOT", StringComparison.OrdinalIgnoreCase))
                {
                    throw reader.Error($"HIERARCHY の直後は ROOT のはずが \"{keyword}\" でした");
                }
                endSite = false;
            }
            else if (string.Equals(keyword, "JOINT", StringComparison.OrdinalIgnoreCase))
            {
                endSite = false;
            }
            else if (string.Equals(keyword, "End", StringComparison.OrdinalIgnoreCase))
            {
                reader.Expect("Site");
                endSite = true;
            }
            else
            {
                throw reader.Error($"JOINT か End Site のはずが \"{keyword}\" でした");
            }

            var joint = new BvhJoint
            {
                Parent = parent,
                IsEndSite = endSite,
                // End Site には名前が無い。親の名前から作らないと Transform 名が衝突する。
                Name = endSite ? parent.Name + "_End" : reader.Next(),
            };

            parent?.Children.Add(joint);
            joints.Add(joint);

            reader.Expect("{");

            while (true)
            {
                string token = reader.Peek();

                if (token == "}")
                {
                    reader.Next();
                    break;
                }

                if (string.Equals(token, "OFFSET", StringComparison.OrdinalIgnoreCase))
                {
                    reader.Next();
                    joint.Offset = new Vector3(reader.NextFloat(), reader.NextFloat(), reader.NextFloat());
                    continue;
                }

                if (string.Equals(token, "CHANNELS", StringComparison.OrdinalIgnoreCase))
                {
                    reader.Next();
                    int count = reader.NextInt();
                    for (int i = 0; i < count; i++)
                    {
                        joint.Channels.Add(ParseChannel(reader.Next(), reader));
                    }
                    continue;
                }

                ParseJoint(reader, joint, isRoot: false);
            }

            return joint;
        }

        private static BvhChannel ParseChannel(string token, TokenReader reader)
        {
            switch (token.ToUpperInvariant())
            {
                case "XPOSITION": return BvhChannel.PositionX;
                case "YPOSITION": return BvhChannel.PositionY;
                case "ZPOSITION": return BvhChannel.PositionZ;
                case "XROTATION": return BvhChannel.RotationX;
                case "YROTATION": return BvhChannel.RotationY;
                case "ZROTATION": return BvhChannel.RotationZ;
                default: throw reader.Error($"知らない CHANNELS の種類です: \"{token}\"");
            }
        }

        /// <summary>各ジョイントに MOTION 行の読み出し開始列を割り当てる。</summary>
        private void AssignChannelColumns()
        {
            int column = 0;
            foreach (BvhJoint joint in joints)
            {
                joint.ChannelStart = column;
                column += joint.Channels.Count;
            }
            ChannelCount = column;
        }

        // ─────────────────────────────────────────────────────────────
        //  MOTION
        // ─────────────────────────────────────────────────────────────

        private void ParseMotion(TokenReader reader)
        {
            reader.Expect("Frames:");
            int frameCount = reader.NextInt();

            reader.Expect("Frame");
            reader.Expect("Time:");
            FrameTime = reader.NextFloat();

            if (FrameTime <= 0f)
            {
                throw reader.Error($"Frame Time が 0 以下です: {FrameTime}");
            }

            for (int i = 0; i < frameCount; i++)
            {
                var row = new float[ChannelCount];
                for (int c = 0; c < ChannelCount; c++)
                {
                    if (!reader.TryNextFloat(out row[c]))
                    {
                        // 途中で切れているファイルは珍しくない。読めたところまでを使う。
                        Debug.LogWarning($"[FBX Animation Baker] BVH の MOTION が {i} フレーム目で尽きました "
                                         + $"(宣言は {frameCount} フレーム)。読めたぶんだけ使います。");
                        return;
                    }
                }
                frames.Add(row);
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  トークン読み
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 空白区切りのトークンを順に返すだけの読み手。
        ///
        /// BVH は改行の位置に意味が無く、"Frames:" のようにコロン込みで 1 語だったり
        /// "Frames :" と離れていたりする。行ではなく語で読むと、その揺れを吸収できる。
        /// </summary>
        private sealed class TokenReader
        {
            private static readonly char[] Separators = { ' ', '\t', '\r', '\n' };

            private readonly string[] tokens;
            private readonly string sourceName;
            private int index;

            public TokenReader(string text, string sourceName)
            {
                this.sourceName = sourceName;
                tokens = text.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            }

            public string Peek() => index < tokens.Length ? tokens[index] : null;

            public string Next()
            {
                if (index >= tokens.Length) throw Error("ファイルが途中で終わっています");
                return tokens[index++];
            }

            /// <summary>
            /// 期待する語を読む。"Frames:" のようにコロンが離れて書かれていても通す。
            /// </summary>
            public void Expect(string expected)
            {
                string token = Next();
                if (string.Equals(token, expected, StringComparison.OrdinalIgnoreCase)) return;

                // "Frames:" を期待して "Frames" が来たら、次の ":" を吸って一致とみなす。
                if (expected.EndsWith(":", StringComparison.Ordinal)
                    && string.Equals(token, expected.TrimEnd(':'), StringComparison.OrdinalIgnoreCase)
                    && Peek() == ":")
                {
                    Next();
                    return;
                }

                throw Error($"\"{expected}\" のはずが \"{token}\" でした");
            }

            public float NextFloat()
            {
                string token = Next();
                if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                {
                    throw Error($"数値のはずが \"{token}\" でした");
                }
                return value;
            }

            public bool TryNextFloat(out float value)
            {
                value = 0f;
                if (index >= tokens.Length) return false;
                return float.TryParse(tokens[index++], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }

            public int NextInt()
            {
                string token = Next();
                if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                {
                    throw Error($"整数のはずが \"{token}\" でした");
                }
                return value;
            }

            public BvhParseException Error(string message)
            {
                return new BvhParseException($"{sourceName}: {message} (語 {index}/{tokens.Length} 付近)");
            }
        }
    }

    public sealed class BvhParseException : Exception
    {
        public BvhParseException(string message) : base(message) { }
    }
}
