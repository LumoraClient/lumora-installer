// Lumora Setup - the launcher comes from GitHub Releases of LumoraClient/lumora-launcher, like the launcher's own
// self-update (LauncherUpdate.java): a release carries LumoraClient.exe, SHA256SUMS.txt and SHA256SUMS.txt.sig
// (Ed25519 signature of SHA256SUMS.txt, base64). Only an EXE whose SHA-256 is listed in a correctly signed list is used.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Lumora.Setup
{
    internal sealed class Release
    {
        public string Version;
        public string Tag;
        public string ExeUrl;
        public long ExeSize;
        public string SumsUrl;
        public string SigUrl;
    }

    internal static class Online
    {
        public const string Repository = "LumoraClient/lumora-launcher";
        private const string ExeName = "LumoraClient.exe";
        /// Public release key of the launcher (launcher-core/src/main/resources/lumora/update-key.pub, X.509 DER).
        private const string UpdateKey = "MCowBQYDK2VwAyEAkNJx4qPtgb+/FwI2UpkGUNUNgKAnccagZ03e0UDoCns=";

        static Online()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        private static HttpWebRequest Request(string url)
        {
            var r = (HttpWebRequest)WebRequest.Create(url);
            r.UserAgent = "LumoraSetup/" + Program.SetupVersion;
            r.Timeout = 20000;
            r.ReadWriteTimeout = 30000;
            r.AllowAutoRedirect = true;
            return r;
        }

        public static string Text(string url)
        {
            HttpWebRequest r = Request(url);
            r.Accept = "application/vnd.github+json";
            using (var resp = (HttpWebResponse)r.GetResponse())
            using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        /// Newest release (GitHub REST API, no account needed).
        public static Release Latest()
        {
            string json;
            try
            {
                json = Text("https://api.github.com/repos/" + Repository + "/releases/latest");
            }
            catch (WebException e)
            {
                throw new SetupException("Keine Verbindung zu GitHub (" + e.Message + "). Für die Installation braucht Lumora Internet.", true);
            }
            var o = new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (o == null)
            {
                throw new SetupException("Antwort von GitHub nicht lesbar.", true);
            }
            var rel = new Release();
            rel.Tag = o["tag_name"] as string ?? "";
            rel.Version = rel.Tag.TrimStart('v');
            foreach (object a in (o["assets"] as object[]) ?? new object[0])
            {
                var asset = a as Dictionary<string, object>;
                if (asset == null)
                {
                    continue;
                }
                string name = asset["name"] as string;
                string url = asset["browser_download_url"] as string;
                if (name == ExeName)
                {
                    rel.ExeUrl = url;
                    rel.ExeSize = Convert.ToInt64(asset["size"]);
                }
                else if (name == "SHA256SUMS.txt")
                {
                    rel.SumsUrl = url;
                }
                else if (name == "SHA256SUMS.txt.sig")
                {
                    rel.SigUrl = url;
                }
            }
            if (rel.ExeUrl == null || rel.SumsUrl == null || rel.SigUrl == null)
            {
                throw new SetupException("Das neueste Lumora-Release (" + rel.Tag + ") ist unvollständig. Bitte später erneut versuchen.", true);
            }
            return rel;
        }

        /// The launcher's version list (profiles.json) of exactly this release.
        public static List<Version> Profiles(Release rel, out string recommended)
        {
            string json = Text("https://raw.githubusercontent.com/" + Repository + "/" + rel.Tag
                + "/launcher-core/src/main/resources/lumora/profiles.json");
            var o = (Dictionary<string, object>)new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(json);
            recommended = o["defaultProfile"] as string;
            var list = new List<Version>();
            foreach (object p in (object[])o["profiles"])
            {
                var d = (Dictionary<string, object>)p;
                var v = new Version();
                v.Id = d["id"] as string;
                v.Minecraft = d["minecraftVersion"] as string;
                list.Add(v);
            }
            return list;
        }

        /// Downloads LumoraClient.exe into the setup's cache (reused if it is already there and verified).
        public static string Download(Release rel, Action<double, string> progress)
        {
            string sums = Text(rel.SumsUrl);
            string sig = Text(rel.SigUrl).Trim();
            if (!Ed25519.Verify(Convert.FromBase64String(sig), Encoding.UTF8.GetBytes(sums), RawKey()))
            {
                throw new SetupException("Die Signatur des Lumora-Releases ist ungültig. Aus Sicherheitsgründen wird nichts installiert.", false);
            }
            string expected = Expected(sums, ExeName);
            if (expected == null)
            {
                throw new SetupException("LumoraClient.exe steht nicht in der signierten Prüfsummenliste.", false);
            }
            string dir = Path.Combine(Path.GetTempPath(), "lumora-setup");
            Directory.CreateDirectory(dir);
            // stored as .bin: the setup only reads the file (PeResources), it never runs it
            string target = Path.Combine(dir, "LumoraClient-" + rel.Version + ".bin");
            if (File.Exists(target) && Sha256(target) == expected)
            {
                progress(1, "Schon heruntergeladen");
                return target;
            }
            string part = target + ".part";
            HttpWebRequest r = Request(rel.ExeUrl);
            try
            {
                using (var resp = (HttpWebResponse)r.GetResponse())
                using (Stream inp = resp.GetResponseStream())
                using (FileStream outp = File.Create(part))
                {
                    long total = resp.ContentLength > 0 ? resp.ContentLength : rel.ExeSize;
                    var buffer = new byte[1 << 16];
                    long done = 0;
                    int n;
                    DateTime last = DateTime.MinValue;
                    while ((n = inp.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        outp.Write(buffer, 0, n);
                        done += n;
                        if ((DateTime.Now - last).TotalMilliseconds > 120)
                        {
                            last = DateTime.Now;
                            progress(total > 0 ? done / (double)total : -1, Engine.Size(done) + " von " + Engine.Size(total));
                        }
                    }
                }
            }
            catch (WebException e)
            {
                throw new SetupException("Download fehlgeschlagen (" + e.Message + "). Bitte Internetverbindung prüfen.", true);
            }
            catch (IOException e)
            {
                throw new SetupException("Download fehlgeschlagen (" + e.Message + ").", true);
            }
            if (Sha256(part) != expected)
            {
                File.Delete(part);
                throw new SetupException("Die heruntergeladene Datei ist beschädigt (Prüfsumme falsch). Bitte erneut versuchen.", true);
            }
            if (File.Exists(target))
            {
                File.Delete(target);
            }
            File.Move(part, target);
            return target;
        }

        private static string Expected(string sums, string file)
        {
            foreach (string raw in sums.Split('\n'))
            {
                string line = raw.Trim();
                int sp = line.IndexOf(' ');
                if (sp == 64 && line.Substring(sp).Trim().TrimStart('*') == file)
                {
                    return line.Substring(0, 64).ToLowerInvariant();
                }
            }
            return null;
        }

        public static string Sha256(string file)
        {
            using (FileStream f = File.OpenRead(file))
            {
                return Sha256(f);
            }
        }

        public static string Sha256(Stream s)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return string.Concat(sha.ComputeHash(s).Select(b => b.ToString("x2")));
            }
        }

        private static byte[] RawKey()
        {
            byte[] der = Convert.FromBase64String(UpdateKey);
            return der.Skip(der.Length - 32).ToArray();
        }
    }

    /// Ed25519 signature verification (RFC 8032, section 5.1.7), extended coordinates over BigInteger.
    /// Only verification of public data, no secret keys: speed and constant time do not matter here.
    internal static class Ed25519
    {
        private static readonly BigInteger P = BigInteger.Pow(2, 255) - 19;
        private static readonly BigInteger L = BigInteger.Pow(2, 252) + BigInteger.Parse("27742317777372353535851937790883648493");
        private static readonly BigInteger D = Mod(-121665 * Inv(121666));
        private static readonly BigInteger I = BigInteger.ModPow(2, (P - 1) / 4, P);
        private static readonly BigInteger[] B = BasePoint();

        private static BigInteger Mod(BigInteger a)
        {
            BigInteger r = a % P;
            return r.Sign < 0 ? r + P : r;
        }

        private static BigInteger Inv(BigInteger a)
        {
            return BigInteger.ModPow(Mod(a), P - 2, P);
        }

        private static BigInteger FromLittleEndian(byte[] b, int offset, int count)
        {
            var bytes = new byte[count + 1];
            Array.Copy(b, offset, bytes, 0, count);
            return new BigInteger(bytes);
        }

        private static BigInteger[] BasePoint()
        {
            BigInteger y = Mod(4 * Inv(5));
            BigInteger x = RecoverX(y, 0);
            return new[] { x, y, BigInteger.One, Mod(x * y) };
        }

        /// x for y and the sign bit, or -1 if there is none.
        private static BigInteger RecoverX(BigInteger y, int sign)
        {
            if (y >= P)
            {
                return BigInteger.MinusOne;
            }
            BigInteger x2 = Mod((y * y - 1) * Inv(D * y * y + 1));
            if (x2.IsZero)
            {
                return sign == 1 ? BigInteger.MinusOne : BigInteger.Zero;
            }
            BigInteger x = BigInteger.ModPow(x2, (P + 3) / 8, P);
            if (!Mod(x * x - x2).IsZero)
            {
                x = Mod(x * I);
            }
            if (!Mod(x * x - x2).IsZero)
            {
                return BigInteger.MinusOne;
            }
            if ((int)(x & 1) != sign)
            {
                x = P - x;
            }
            return x;
        }

        private static BigInteger[] Decode(byte[] b, int offset)
        {
            var copy = new byte[32];
            Array.Copy(b, offset, copy, 0, 32);
            int sign = copy[31] >> 7;
            copy[31] &= 0x7F;
            BigInteger y = FromLittleEndian(copy, 0, 32);
            BigInteger x = RecoverX(y, sign);
            if (x.Sign < 0)
            {
                return null;
            }
            return new[] { x, y, BigInteger.One, Mod(x * y) };
        }

        private static BigInteger[] Add(BigInteger[] p, BigInteger[] q)
        {
            BigInteger a = Mod((p[1] - p[0]) * (q[1] - q[0]));
            BigInteger b = Mod((p[1] + p[0]) * (q[1] + q[0]));
            BigInteger c = Mod(p[3] * 2 * D * q[3]);
            BigInteger d = Mod(p[2] * 2 * q[2]);
            BigInteger e = b - a, f = d - c, g = d + c, h = b + a;
            return new[] { Mod(e * f), Mod(g * h), Mod(f * g), Mod(e * h) };
        }

        private static BigInteger[] Multiply(BigInteger s, BigInteger[] p)
        {
            BigInteger[] q = { BigInteger.Zero, BigInteger.One, BigInteger.One, BigInteger.Zero };
            while (s > 0)
            {
                if (!s.IsEven)
                {
                    q = Add(q, p);
                }
                p = Add(p, p);
                s >>= 1;
            }
            return q;
        }

        private static bool Equal(BigInteger[] p, BigInteger[] q)
        {
            return Mod(p[0] * q[2] - q[0] * p[2]).IsZero && Mod(p[1] * q[2] - q[1] * p[2]).IsZero;
        }

        public static bool Verify(byte[] signature, byte[] message, byte[] publicKey)
        {
            if (signature == null || signature.Length != 64 || publicKey == null || publicKey.Length != 32)
            {
                return false;
            }
            BigInteger[] a = Decode(publicKey, 0);
            BigInteger[] r = Decode(signature, 0);
            if (a == null || r == null)
            {
                return false;
            }
            BigInteger s = FromLittleEndian(signature, 32, 32);
            if (s >= L)
            {
                return false;
            }
            byte[] h;
            using (SHA512 sha = SHA512.Create())
            {
                var data = new byte[64 + message.Length];
                Array.Copy(signature, 0, data, 0, 32);
                Array.Copy(publicKey, 0, data, 32, 32);
                Array.Copy(message, 0, data, 64, message.Length);
                h = sha.ComputeHash(data);
            }
            BigInteger k = FromLittleEndian(h, 0, 64) % L;
            return Equal(Multiply(s, B), Add(r, Multiply(k, a)));
        }
    }
}
