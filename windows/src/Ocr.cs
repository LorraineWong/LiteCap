// Offline text recognition using the OCR engine built into Windows 10/11 (Windows.Media.Ocr).
// WinRT types are resolved at run time through reflection, so the project still builds with the
// plain .NET Framework compiler and needs no extra references or downloads.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LiteCap
{
    class OcrUnavailableException : Exception
    {
        public OcrUnavailableException(string message) : base(message) { }
    }

    class OcrOutput
    {
        public string Text;
        public int Lines;
        public string Language;
        public List<string> LineList = new List<string>();
        public List<RectangleF> Boxes = new List<RectangleF>();   // per line, in source-image pixels (may be empty)
    }

    static class WinOcr
    {
        // The assembly part of a WinRT type name is only a hint; the runtime locates the .winmd by namespace.
        static readonly string[] Hosts = { "Windows.Foundation", "Windows.Media", "Windows.Storage", "Windows" };
        static MethodInfo asTaskGeneric;

        static Type WinRT(string fullName)
        {
            foreach (string host in Hosts)
            {
                Type t = null;
                try { t = Type.GetType(fullName + ", " + host + ", ContentType=WindowsRuntime", false); }
                catch { }
                if (t != null) return t;
            }
            throw new OcrUnavailableException("Windows component not found: " + fullName);
        }

        // Invokes a WinRT method returning IAsyncOperation<T> and blocks until T is available.
        static object Await(MethodInfo method, object target, params object[] args)
        {
            if (method == null) throw new OcrUnavailableException("Windows OCR API not available on this system.");
            object op = method.Invoke(target, args);
            if (asTaskGeneric == null)
            {
                Assembly rt = Assembly.Load("System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");
                Type ext = rt.GetType("System.WindowsRuntimeSystemExtensions", true);
                foreach (MethodInfo m in ext.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    ParameterInfo[] ps = m.GetParameters();
                    if (m.Name == "AsTask" && m.IsGenericMethodDefinition && ps.Length == 1 && ps[0].ParameterType.Name == "IAsyncOperation`1")
                    {
                        asTaskGeneric = m;
                        break;
                    }
                }
                if (asTaskGeneric == null) throw new OcrUnavailableException("WinRT async bridge not found.");
            }
            Type resultType = method.ReturnType.GetGenericArguments()[0];
            Task task = (Task)asTaskGeneric.MakeGenericMethod(resultType).Invoke(null, new object[] { op });
            task.Wait();
            return task.GetType().GetProperty("Result").GetValue(task, null);
        }

        static object CreateEngine(Type tEngine, Type tLang, out string languageName)
        {
            languageName = null;
            MethodInfo supported = tEngine.GetMethod("IsLanguageSupported");
            MethodInfo fromLang = tEngine.GetMethod("TryCreateFromLanguage");
            // Chinese engines also read Latin text, so prefer them for mixed screenshots.
            foreach (string tag in new string[] { "zh-Hans-CN", "zh-Hans", "zh-CN", "zh-Hant", "en-US", "en" })
            {
                object lang;
                try { lang = Activator.CreateInstance(tLang, tag); }
                catch { continue; }
                if ((bool)supported.Invoke(null, new object[] { lang }))
                {
                    object engine = fromLang.Invoke(null, new object[] { lang });
                    if (engine != null)
                    {
                        languageName = (string)tLang.GetProperty("DisplayName").GetValue(lang, null);
                        return engine;
                    }
                }
            }
            object fallback = tEngine.GetMethod("TryCreateFromUserProfileLanguages").Invoke(null, null);
            if (fallback != null)
            {
                object rl = tEngine.GetProperty("RecognizerLanguage").GetValue(fallback, null);
                if (rl != null) languageName = (string)tLang.GetProperty("DisplayName").GetValue(rl, null);
            }
            return fallback;
        }

        // Scales the image into the engine's comfort zone: small text is enlarged, oversized images shrunk.
        static Bitmap Prepare(Bitmap src, int maxDim, out double scale)
        {
            scale = 1.0;
            int longest = Math.Max(src.Width, src.Height);
            if (longest * 2 <= maxDim && src.Width * src.Height < 2000000) scale = 2.0;
            if (longest * scale > maxDim) scale = (double)maxDim / longest;
            int w = Math.Max(1, (int)(src.Width * scale)), h = Math.Max(1, (int)(src.Height * scale));
            Bitmap dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, w, h));
            }
            return dst;
        }

        // Union of the word rectangles of one line, mapped back to the unscaled capture. Empty if unavailable.
        static RectangleF LineBox(object words, double scale)
        {
            RectangleF box = RectangleF.Empty;
            IEnumerable list = words as IEnumerable;
            if (list == null) return box;
            try
            {
                foreach (object w in list)
                {
                    object r = w.GetType().GetProperty("BoundingRect").GetValue(w, null);
                    Type rt = r.GetType();
                    float x = (float)(Convert.ToDouble(rt.GetProperty("X").GetValue(r, null)) / scale);
                    float y = (float)(Convert.ToDouble(rt.GetProperty("Y").GetValue(r, null)) / scale);
                    float wd = (float)(Convert.ToDouble(rt.GetProperty("Width").GetValue(r, null)) / scale);
                    float ht = (float)(Convert.ToDouble(rt.GetProperty("Height").GetValue(r, null)) / scale);
                    RectangleF wr = new RectangleF(x, y, wd, ht);
                    box = box.IsEmpty ? wr : RectangleF.Union(box, wr);
                }
            }
            catch { return RectangleF.Empty; }
            return box;
        }

        // Joins wrapped lines into paragraphs: no space between CJK characters, one space otherwise;
        // a line ending in sentence punctuation starts a new paragraph.
        public static string MergeLines(List<string> lines)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                string cur = lines[i];
                if (i == 0) { sb.Append(cur); continue; }
                string prev = lines[i - 1];
                char last = prev.Length > 0 ? prev[prev.Length - 1] : ' ';
                char first = cur.Length > 0 ? cur[0] : ' ';
                if ("。！？.!?:：;；".IndexOf(last) >= 0) sb.Append("\r\n");
                else if (!(IsCjk(last) && IsCjk(first)) && last != '-') sb.Append(' ');
                if (last == '-' && sb.Length > 0) sb.Length--;   // re-join hyphenated words
                sb.Append(cur);
            }
            return sb.ToString();
        }

        static bool IsCjk(char c)
        {
            return (c >= 0x3000 && c <= 0x303f) || (c >= 0x3400 && c <= 0x9fff) || (c >= 0xf900 && c <= 0xfaff) || (c >= 0xff00 && c <= 0xffef);
        }

        static readonly Regex CjkGap = new Regex(@"(?<=[　-〿㐀-鿿豈-﫿＀-￯])\s+(?=[　-〿㐀-鿿豈-﫿＀-￯])");

        // Windows OCR separates CJK characters with spaces; join them back together.
        public static string TidyLine(string line)
        {
            return CjkGap.Replace(line ?? "", "").Trim();
        }

        public static OcrOutput Recognize(Bitmap image)
        {
            try { return RecognizeCore(image); }
            catch (TargetInvocationException ex) { throw Unwrap(ex); }
            catch (AggregateException ex) { throw Unwrap(ex); }
        }

        static Exception Unwrap(Exception ex)
        {
            while ((ex is TargetInvocationException || ex is AggregateException) && ex.InnerException != null) ex = ex.InnerException;
            return ex;
        }

        static OcrOutput RecognizeCore(Bitmap image)
        {
            Type tFile = WinRT("Windows.Storage.StorageFile");
            Type tMode = WinRT("Windows.Storage.FileAccessMode");
            Type tStream = WinRT("Windows.Storage.Streams.IRandomAccessStream");
            Type tDecoder = WinRT("Windows.Graphics.Imaging.BitmapDecoder");
            Type tEngine = WinRT("Windows.Media.Ocr.OcrEngine");
            Type tResult = WinRT("Windows.Media.Ocr.OcrResult");
            Type tLine = WinRT("Windows.Media.Ocr.OcrLine");
            Type tLang = WinRT("Windows.Globalization.Language");

            string lang;
            object engine = CreateEngine(tEngine, tLang, out lang);
            if (engine == null) throw new OcrUnavailableException(L.T("ocr_unavailable"));

            int maxDim = 2600;
            try { maxDim = Convert.ToInt32(tEngine.GetProperty("MaxImageDimension").GetValue(null, null)); } catch { }

            string tmp = Path.Combine(Path.GetTempPath(), "litecap-ocr-" + Guid.NewGuid().ToString("N") + ".png");
            object stream = null;
            try
            {
                double scale;
                using (Bitmap prepared = Prepare(image, maxDim, out scale)) prepared.Save(tmp, ImageFormat.Png);

                object file = Await(tFile.GetMethod("GetFileFromPathAsync"), null, tmp);
                stream = Await(tFile.GetMethod("OpenAsync", new Type[] { tMode }), file, Enum.ToObject(tMode, 0));
                object decoder = Await(tDecoder.GetMethod("CreateAsync", new Type[] { tStream }), null, stream);
                object bitmap = Await(tDecoder.GetMethod("GetSoftwareBitmapAsync", Type.EmptyTypes), decoder);
                object result = Await(tEngine.GetMethod("RecognizeAsync"), engine, bitmap);

                StringBuilder sb = new StringBuilder();
                int count = 0;
                IEnumerable lines = (IEnumerable)tResult.GetProperty("Lines").GetValue(result, null);
                PropertyInfo textProp = tLine.GetProperty("Text");
                PropertyInfo wordsProp = tLine.GetProperty("Words");
                List<string> lineList = new List<string>();
                List<RectangleF> boxes = new List<RectangleF>();
                foreach (object line in lines)
                {
                    string t = TidyLine((string)textProp.GetValue(line, null));
                    if (t.Length == 0) continue;
                    if (count > 0) sb.Append("\r\n");
                    sb.Append(t);
                    lineList.Add(t);
                    boxes.Add(LineBox(wordsProp == null ? null : wordsProp.GetValue(line, null), scale));
                    count++;
                }
                IDisposable d = bitmap as IDisposable;
                if (d != null) d.Dispose();

                OcrOutput o = new OcrOutput();
                o.Text = sb.ToString();
                o.Lines = count;
                o.Language = lang;
                o.LineList = lineList;
                o.Boxes = boxes;
                return o;
            }
            finally
            {
                IDisposable ds = stream as IDisposable;
                if (ds != null) { try { ds.Dispose(); } catch { } }
                try { File.Delete(tmp); } catch { }
            }
        }
    }
}
