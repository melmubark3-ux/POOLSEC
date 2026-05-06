using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Colors;

[assembly: CommandClass(typeof(POOLSEC.Commands))]

namespace POOLSEC
{
    /// <summary>
    /// مجموعة معاملات المسبح المُدخلة من المستخدم
    /// </summary>
    public class PoolParams
    {
        public double L;           // طول المسبح (م)
        public double W;           // عرض المسبح (م)
        public double Ds;          // عمق ضحل (م) — 0.838-1.219
        public double Dd;          // عمق عميق (م) — > Ds
        public double Ls;          // طول الضحل (م)
        public double Ld;          // طول العميق (م)
        public double t;           // سمك الجدار (م) — >= 0.2
        public double tf;          // سمك الأرضية (م) — >= 0.15
        public double WaterLevel;  // منسوب الماء (م فوق الأرضية الضحلة)
        public double Freeboard;   // ارتفاع الحافة فوق الماء (م)
        public double Scale;       // مقياس الرسم
        public string PoolType;    // S=Skimmer, O=Overflow, H=Hybrid
        public double PumpRoomL;   // طول غرفة المضخات (0 = بدون)
        public double PumpRoomW;   // عرض غرفة المضخات (0 = بدون)
        public double BalanceTankDepth; // عمق خزان التوازن (لـ O/H)
        public double TransitionLen;    // محسوب: طول المنحدر الانتقالي
    }

    public class Commands
    {
        // ──────────────────────── COMMAND ────────────────────────
        [CommandMethod("POOLSEC")]
        public void PoolsecMain()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                PoolParams p = GetInputs(ed);
                if (p == null) return;

                string error = Validate(p, ed);
                if (error != null)
                {
                    ed.WriteMessage($"\n❌ خطأ في التحقق: {error}");
                    return;
                }

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    BlockTableRecord btr = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                    // إنشاء الطبقات
                    ObjectId layerStruct  = CreateLayer(tr, db, "POOL-STRUCTURE", 5);
                    ObjectId layerWater   = CreateLayer(tr, db, "POOL-WATER", 4);
                    ObjectId layerPlumb   = CreateLayer(tr, db, "POOL-PLUMBING", 1);
                    ObjectId layerDim     = CreateLayer(tr, db, "POOL-DIM", 3);
                    ObjectId layerText    = CreateLayer(tr, db, "POOL-TEXT", 7);
                    ObjectId layerHatch   = CreateLayer(tr, db, "POOL-HATCH", 8);

                    double s = 1.0 / p.Scale; // عامل المقياس

                    // رسم كل المشاهد
                    DrawPlanView(tr, btr, p, s, layerStruct, layerPlumb, layerDim, layerText);
                    DrawLongSection(tr, btr, p, s, layerStruct, layerWater, layerHatch, layerDim, layerText);
                    DrawCrossSection(tr, btr, p, s, layerStruct, layerWater, layerHatch, layerDim, layerText);
                    DrawDetailA(tr, btr, p, s, layerStruct, layerHatch, layerDim, layerText);
                    DrawDetailB(tr, btr, p, s, layerStruct, layerPlumb, layerWater, layerHatch, layerDim, layerText);

                    tr.Commit();
                }

                doc.SendStringToExecute("ZOOM _E ", false, false, false);
                ed.WriteMessage("\n✅ تم الانتهاء من رسم المسبح بنجاح!");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n❌ خطأ: {ex.Message}");
            }
        }

        // ──────────────────── إدخال البيانات ────────────────────
        private PoolParams GetInputs(Editor ed)
        {
            var p = new PoolParams();

            ed.WriteMessage("\n══════════════════════════════════════════════");
            ed.WriteMessage("\n   رسم قطاع المسبح - POOLSEC");
            ed.WriteMessage("\n══════════════════════════════════════════════\n");

            // 1. طول المسبح
            var pdo = new PromptDoubleOptions("\n✦ طول المسبح L (م): ");
            pdo.AllowNegative = false; pdo.AllowZero = false; pdo.DefaultValue = "8.0";
            var res = ed.GetDouble(pdo);
            if (res.Status != PromptStatus.OK) return null;
            p.L = res.Value;

            // 2. عرض المسبح
            pdo = new PromptDoubleOptions("\n✦ عرض المسبح W (م): ");
            pdo.AllowNegative = false; pdo.AllowZero = false; pdo.DefaultValue = "3.5";
            res = ed.GetDouble(pdo);
            if (res.Status != PromptStatus.OK) return null;
            p.W = res.Value;

            // 3. عمق ضحل
            pdo = new PromptDoubleOptions("\n✦ العمق الضحل Ds (م) [0.838-1.219]: ");
            pdo.AllowNegative = false; pdo.AllowZero = false; pdo.DefaultValue = "1.0";
            res = ed.GetDouble(pdo);
            if (res.Status != PromptStatus.OK) return null;
            p.Ds = res.Value;

            // 4. عمق عميق
            pdo = new PromptDoubleOptions("\n✦ العمق العميق Dd (م) [أكبر من Ds]: ");
            pdo.AllowNegative = false; pdo.AllowZero = false; pdo.DefaultValue = "1.8";
            res = ed.GetDouble(pdo);
            if (res.Status != PromptStatus.OK) return null;
            p.Dd = res.Value;

            // 5. طول الضحل
            pdo = new PromptDoubleOptions("\n✦ طول القسم الضحل Ls (م): ");
            pdo.AllowNegative = false; pdo.AllowZero = false; pdo.DefaultValue = "3.0";
            res = ed.GetDouble(pdo);
            if (res.Status != PromptStatus.OK) return null;
            p.Ls = res.Value;

            // 6. طول العميق
            pdo = new PromptDoubleOptions("\n✦ طول القسم العميق Ld (م): ");
            pdo.AllowNegative = false; pdo.AllowZero = false; pdo.DefaultValue = "3.0";
            res = ed.GetDouble(pdo);
            if (res.Status != PromptStatus.OK) return null;
            p.Ld = res.Value;

            // 7. سمك الجدار
            pdo = new PromptDoubleOptions("\n✦ سمك الجدار t (م) [>= 0.20]: ");
            pdo.AllowNegative = false; pdo.AllowZero = false; pdo.DefaultValue = "0.25";
            res = ed.GetDouble(pdo);
            if (res.Status != PromptStatus.OK) return null;
            p.t = res.Value;

            // 8. سمك الأرضية
            pdo = new PromptDoubleOptions("\n✦ سمك الأرضية tf (م) [>= 0.15]: ");
            pdo.AllowNegative = false; pdo.AllowZero = false; pdo.DefaultValue = "0.20";
            res = ed.GetDouble(pdo);
            if (res.Status != PromptStatus.OK) return null;
            p.tf = res.Value;

            // 9. منسوب الماء
            pdo = new PromptDoubleOptions("\n✦ منسوب الماء (عمق الماء الفعلي عند الضحل, م): ");
            pdo.AllowNegative = false; pdo.AllowZero = false; pdo.DefaultValue = p.Ds;
            res = ed.GetDouble(pdo);
            if (res.Status != PromptStatus.OK) return null;
            p.WaterLevel = res.Value;

            // 10. ارتفاع الحافة الحرة
            pdo = new PromptDoubleOptions("\n✦ ارتفاع الحافة الحرة Freeboard (م): ");
            pdo.AllowNegative = false; pdo.AllowZero = false; pdo.DefaultValue = "0.15";
            res = ed.GetDouble(pdo);
            if (res.Status != PromptStatus.OK) return null;
            p.Freeboard = res.Value;

            // 11. مقياس الرسم
            pdo = new PromptDoubleOptions("\n✦ مقياس الرسم (50 = 1:50, 100 = 1:100): ");
            pdo.AllowNegative = false; pdo.AllowZero = false; pdo.DefaultValue = "50.0";
            res = ed.GetDouble(pdo);
            if (res.Status != PromptStatus.OK) return null;
            p.Scale = res.Value;

            // 12. نوع المسبح
            var pko = new PromptKeywordOptions("\n✦ نوع المسبح:");
            pko.Keywords.Add("Skimmer");
            pko.Keywords.Add("Overflow");
            pko.Keywords.Add("Hybrid");
            pko.AllowNone = true;
            pko.Default = "Skimmer";
            var kres = ed.GetKeywords(pko);
            if (kres.Status != PromptStatus.OK) return null;
            p.PoolType = kres.StringResult.Substring(0, 1); // S, O, H

            // 13. طول غرفة المضخات (اختياري)
            pdo = new PromptDoubleOptions("\n✦ طول غرفة المضخات (م) [0 = بدون]: ");
            pdo.AllowNegative = false; pdo.AllowZero = true; pdo.DefaultValue = "0";
            res = ed.GetDouble(pdo);
            if (res.Status != PromptStatus.OK) return null;
            p.PumpRoomL = res.Value;

            // 14. عرض غرفة المضخات (اختياري)
            pdo = new PromptDoubleOptions("\n✦ عرض غرفة المضخات (م) [0 = بدون]: ");
            pdo.AllowNegative = false; pdo.AllowZero = true; pdo.DefaultValue = "0";
            res = ed.GetDouble(pdo);
            if (res.Status != PromptStatus.OK) return null;
            p.PumpRoomW = res.Value;

            // 15. عمق خزان التوازن (لـ Overflow/Hybrid فقط)
            if (p.PoolType == "O" || p.PoolType == "H")
            {
                pdo = new PromptDoubleOptions("\n✦ عمق خزان التوازن (م) [لـ Overflow/Hybrid]: ");
                pdo.AllowNegative = false; pdo.AllowZero = true; pdo.DefaultValue = "1.5";
                res = ed.GetDouble(pdo);
                if (res.Status != PromptStatus.OK) return null;
                p.BalanceTankDepth = res.Value;
            }
            else
            {
                p.BalanceTankDepth = 0;
            }

            return p;
        }

        // ──────────────────── التحقق من الصحة ────────────────────
        private string Validate(PoolParams p, Editor ed)
        {
            if (p.Ds < 0.838) return "العمق الضحل أقل من الحد الأدنى (0.838 م).";
            if (p.Ds > 1.219) return "العمق الضحل أكبر من الحد الأقصى (1.219 م).";
            if (p.Dd <= p.Ds) return "العمق العميق يجب أن يكون أكبر من العمق الضحل.";
            if (p.t < 0.20) return "سمك الجدار لا يقل عن 0.20 م.";
            if (p.tf < 0.15) return "سمك الأرضية لا يقل عن 0.15 م.";
            if (p.Ls + p.Ld > p.L) return "مجموع Ls + Ld أكبر من الطول الكلي L.";

            // حساب المنحدر الانتقالي (ISPSC 2024: أقصى ميل 1:7)
            double depthDiff = p.Dd - p.Ds;
            double minTransLen = depthDiff * 7.0; // 1:7
            double availableLen = p.L - p.Ls - p.Ld;

            if (availableLen < minTransLen)
            {
                ed.WriteMessage($"\n⚠️ تحذير: المسافة المتبقية للانتقال {availableLen:F2}م أقل من المطلوب ({minTransLen:F2}م) بميل 1:7.");
                ed.WriteMessage($"\n   الميل سيكون 1:{availableLen / depthDiff:F2} بدلاً من 1:7.");
                p.TransitionLen = availableLen;
            }
            else
            {
                p.TransitionLen = minTransLen;
            }

            ed.WriteMessage($"\n✓ طول الانتقال المحسوب: {p.TransitionLen:F2} م");
            return null;
        }

        // ═══════════════════ الرسم ═══════════════════

        // ─────────── 1. المسقط الأفقي (Plan View) ───────────
        private void DrawPlanView(Transaction tr, BlockTableRecord btr, PoolParams p, double s,
            ObjectId ls, ObjectId lp, ObjectId ld, ObjectId lt)
        {
            double ox = 0;
            double oy = 100 * s;
            double textH = 0.08 * s;
            double dimOff = 0.3 * s;

            // مستطيل المسبح
            Point2dCollection poolRect = new Point2dCollection
            {
                new Point2d(ox, oy),
                new Point2d(ox + p.L * s, oy),
                new Point2d(ox + p.L * s, oy + p.W * s),
                new Point2d(ox, oy + p.W * s)
            };
            ObjectId poolPolyId = AddLWPolyline(tr, btr, poolRect, ls, true);

            // خط تقسيم الضحل/العميق
            double divX = ox + p.Ls * s;
            AddLine(tr, btr, new Point3d(divX, oy, 0), new Point3d(divX, oy + p.W * s, 0), ls);

            // خط الانتقال
            double transX = ox + (p.Ls + p.TransitionLen) * s;
            AddLine(tr, btr, new Point3d(transX, oy, 0), new Point3d(transX, oy + p.W * s, 0), ls);

            // رموز تغذية (دائرة مع X) — عند الضحل
            double feedX = ox + (p.Ls * 0.5) * s;
            double feedY = oy + (p.W * 0.7) * s;
            ObjectId feedCirc = AddCircle(tr, btr, new Point3d(feedX, feedY, 0), 0.03 * s, lp);
            AddLine(tr, btr, new Point3d(feedX - 0.03 * s, feedY - 0.03 * s, 0),
                          new Point3d(feedX + 0.03 * s, feedY + 0.03 * s, 0), lp);
            AddLine(tr, btr, new Point3d(feedX + 0.03 * s, feedY - 0.03 * s, 0),
                          new Point3d(feedX - 0.03 * s, feedY + 0.03 * s, 0), lp);
            AddText(tr, btr, "تغذية", new Point3d(feedX + 0.05 * s, feedY, 0), textH, lt);

            // رموز صرف (دائرة) — عند العميق
            double drainX = ox + (p.Ls + p.TransitionLen + p.Ld * 0.5) * s;
            double drainY = oy + (p.W * 0.3) * s;
            AddCircle(tr, btr, new Point3d(drainX, drainY, 0), 0.04 * s, lp);
            AddText(tr, btr, "صرف", new Point3d(drainX + 0.05 * s, drainY, 0), textH, lt);

            // غرفة مضخات
            if (p.PumpRoomL > 0 && p.PumpRoomW > 0)
            {
                double prX = ox + p.L * s + 0.5 * s;
                double prY = oy;
                Point2dCollection prRect = new Point2dCollection
                {
                    new Point2d(prX, prY),
                    new Point2d(prX + p.PumpRoomL * s, prY),
                    new Point2d(prX + p.PumpRoomL * s, prY + p.PumpRoomW * s),
                    new Point2d(prX, prY + p.PumpRoomW * s)
                };
                AddLWPolyline(tr, btr, prRect, ls, true);
                AddText(tr, btr, "غرفة مضخات", new Point3d(prX + 0.1 * s, prY + p.PumpRoomW * s * 0.45, 0), textH, lt);
            }

            // أبعاد المسقط
            double dimY = oy - dimOff;
            Point3d dp1 = new Point3d(ox, dimY, 0);
            Point3d dp2 = new Point3d(ox + p.L * s, dimY, 0);
            Point3d dpLine = new Point3d(ox + p.L * s * 0.5, dimY - dimOff, 0);
            AddAlignedDim(tr, btr, dp1, dp2, dpLine, ld);

            // أبعاد التقسيم
            double dimDivY = oy - dimOff * 2;
            Point3d dpDiv1 = new Point3d(ox, dimDivY, 0);
            Point3d dpDiv2 = new Point3d(divX, dimDivY, 0);
            Point3d dpDivL = new Point3d(ox + p.Ls * s * 0.5, dimDivY - dimOff, 0);
            AddAlignedDim(tr, btr, dpDiv1, dpDiv2, dpDivL, ld);

            Point3d dpTrans1 = new Point3d(divX, dimDivY, 0);
            Point3d dpTrans2 = new Point3d(transX, dimDivY, 0);
            Point3d dpTransL = new Point3d((divX + transX) * 0.5, dimDivY - dimOff, 0);
            AddAlignedDim(tr, btr, dpTrans1, dpTrans2, dpTransL, ld);

            // بعد العرض (يمين)
            double dimWX = ox + p.L * s + dimOff;
            Point3d dw1 = new Point3d(dimWX, oy, 0);
            Point3d dw2 = new Point3d(dimWX, oy + p.W * s, 0);
            Point3d dwLine = new Point3d(dimWX + dimOff, oy + p.W * s * 0.5, 0);
            AddAlignedDim(tr, btr, dw1, dw2, dwLine, ld);

            // نصوص تسمية
            AddText(tr, btr, "مسقط أفقي", new Point3d(ox, oy - dimOff * 4, 0), textH * 1.3, lt);
            AddText(tr, btr, $"S = 1:{p.Scale}", new Point3d(ox + p.L * s * 0.7, oy - dimOff * 4, 0), textH, lt);
            AddText(tr, btr, "ضحل", new Point3d(ox + p.Ls * s * 0.35, oy + p.W * s * 1.1, 0), textH, lt);
            AddText(tr, btr, "انتقال", new Point3d((divX + transX) * 0.5, oy + p.W * s * 1.1, 0), textH * 0.8, lt);
            AddText(tr, btr, "عميق", new Point3d((transX + ox + p.L * s) * 0.5, oy + p.W * s * 1.1, 0), textH, lt);
        }

        // ─────────── 2. القطاع الطولي A-A ───────────
        private void DrawLongSection(Transaction tr, BlockTableRecord btr, PoolParams p, double s,
            ObjectId ls, ObjectId lw, ObjectId lh, ObjectId ld, ObjectId lt)
        {
            double ox = 0;
            double oy = 0;
            double textH = 0.08 * s;
            double dimOff = 0.3 * s;

            // حساب النقاط الرئيسية
            double wallTop    = oy + (p.Freeboard + p.WaterLevel) * s;
            double waterLine  = oy + p.Freeboard * s;
            double floorShallow = oy; // منسوب أرضية الضحل
            double floorDeep    = oy - (p.Dd - p.Ds) * s;

            double xLeftWall  = ox + p.t * s;
            double xShallowEnd = xLeftWall + p.Ls * s;
            double xTransEnd   = xShallowEnd + p.TransitionLen * s;
            double xDeepEnd    = xTransEnd + p.Ld * s;

            // ─── الكسوة الخارجية (السطح الخارجي) ───
            // الجدار الأيسر
            Point2dCollection outerShell = new Point2dCollection();
            double extTop = wallTop;
            double extLeft = ox;
            double extRight = xDeepEnd + p.t * s;

            // خارجي: يسار → تحت الضحل → تحت العميق → يمين → فوق
            outerShell.Add(new Point2d(extLeft, wallTop));
            outerShell.Add(new Point2d(extLeft, floorShallow - p.tf * s));
            outerShell.Add(new Point2d(extRight, floorDeep - p.tf * s));
            outerShell.Add(new Point2d(extRight, wallTop));
            outerShell.Add(new Point2d(extLeft, wallTop)); // Close

            // ─── السطح الداخلي ───
            Point2dCollection innerShell = new Point2dCollection();
            innerShell.Add(new Point2d(xLeftWall, wallTop));
            innerShell.Add(new Point2d(xLeftWall, floorShallow));
            innerShell.Add(new Point2d(xShallowEnd, floorShallow));
            innerShell.Add(new Point2d(xTransEnd, floorDeep));
            innerShell.Add(new Point2d(xDeepEnd, floorDeep));
            innerShell.Add(new Point2d(xDeepEnd, wallTop));
            innerShell.Add(new Point2d(xLeftWall, wallTop)); // Close

            // رسم السطح الداخلي
            ObjectId innerPolyId = AddLWPolyline(tr, btr, innerShell, ls, true);

            // رسم السطح الخارجي  
            ObjectId outerPolyId = AddLWPolyline(tr, btr, outerShell, ls, true);

            // خط سطح الأرض (أسفل الحفريات)
            double groundOffset = -0.2 * s;
            double groundY = floorShallow - p.tf * s + groundOffset;
            AddLine(tr, btr, new Point3d(ox - 0.3 * s, groundY, 0),
                           new Point3d(extRight + 0.3 * s, groundY, 0), ls);

            // ─── منطقة المياه ───
            Point2dCollection waterPoly = new Point2dCollection();
            waterPoly.Add(new Point2d(xLeftWall, waterLine));
            waterPoly.Add(new Point2d(xShallowEnd, waterLine));
            waterPoly.Add(new Point2d(xTransEnd, waterLine - (p.Dd - p.Ds) * s));
            waterPoly.Add(new Point2d(xDeepEnd, waterLine - (p.Dd - p.Ds) * s));
            waterPoly.Add(new Point2d(xLeftWall, waterLine));
            ObjectId waterPolyId = AddLWPolyline(tr, btr, waterPoly, lw, true);

            // تعبئة المياه
            ObjectIdCollection waterBound = new ObjectIdCollection { waterPolyId };
            AddHatch(tr, btr, "ANGLE", 0.5 * s, waterBound, lw);

            // ─── خزان التوازن (لـ Overflow/Hybrid) ───
            if ((p.PoolType == "O" || p.PoolType == "H") && p.BalanceTankDepth > 0)
            {
                double btX = extRight + 0.3 * s;
                double btY = floorDeep - p.tf * s;
                double btDepth = p.BalanceTankDepth * s;

                Point2dCollection btRect = new Point2dCollection
                {
                    new Point2d(btX, btY),
                    new Point2d(btX + 1.0 * s, btY),
                    new Point2d(btX + 1.0 * s, btY - btDepth),
                    new Point2d(btX, btY - btDepth)
                };
                AddLWPolyline(tr, btr, btRect, ls, true);
                AddText(tr, btr, "خزان توازن", new Point3d(btX + 0.1 * s, btY - btDepth * 0.5, 0), textH, lt);

                // خط ربط الخزان بالمسبح
                double connY = btY - btDepth * 0.3;
                AddLine(tr, btr, new Point3d(xDeepEnd, connY, 0), new Point3d(btX, connY, 0), ls);

                // بعد عمق الخزان
                Point3d btDim1 = new Point3d(btX + 1.0 * s + dimOff, btY, 0);
                Point3d btDim2 = new Point3d(btX + 1.0 * s + dimOff, btY - btDepth, 0);
                Point3d btDL   = new Point3d(btX + 1.0 * s + dimOff + dimOff, btY - btDepth * 0.5, 0);
                AddAlignedDim(tr, btr, btDim1, btDim2, btDL, ld);
            }

            // ─── تسمية وترقيم المنحدر ───
            double slopeMidX = (xShallowEnd + xTransEnd) * 0.5;
            double slopeMidY = (floorShallow + floorDeep) * 0.5;
            AddText(tr, btr, $"ميل 1:{p.TransitionLen / (p.Dd - p.Ds):F1}",
                    new Point3d(slopeMidX, slopeMidY + 0.15 * s, 0), textH, lt);

            // ─── أبعاد ───
            double dimY = oy - dimOff - (p.Dd - p.Ds) * s;

            // بعد الطول الكلي
            Point3d dLen1 = new Point3d(ox, floorDeep - p.tf * s - 0.3 * s, 0);
            Point3d dLen2 = new Point3d(extRight, floorDeep - p.tf * s - 0.3 * s, 0);
            Point3d dLenL = new Point3d(extRight * 0.5, floorDeep - p.tf * s - 0.5 * s, 0);
            AddAlignedDim(tr, btr, dLen1, dLen2, dLenL, ld);

            // بعد الجدار الأيسر من الداخل
            Point3d dWall1 = new Point3d(extLeft, floorShallow, 0);
            Point3d dWall2 = new Point3d(extLeft, wallTop, 0);
            Point3d dWallL = new Point3d(extLeft - dimOff, (floorShallow + wallTop) * 0.5, 0);
            AddAlignedDim(tr, btr, dWall1, dWall2, dWallL, ld);

            // بعد العمق الضحل
            Point3d dDs1 = new Point3d(extLeft - dimOff * 2, floorShallow, 0);
            Point3d dDs2 = new Point3d(extLeft - dimOff * 2, waterLine, 0);
            Point3d dDsL = new Point3d(extLeft - dimOff * 3, (floorShallow + waterLine) * 0.5, 0);
            AddAlignedDim(tr, btr, dDs1, dDs2, dDsL, ld);

            // بعد العمق العميق
            Point3d dDd1 = new Point3d(extRight + dimOff, floorDeep, 0);
            Point3d dDd2 = new Point3d(extRight + dimOff, waterLine - (p.Dd - p.Ds) * s, 0);
            Point3d dDdL = new Point3d(extRight + dimOff * 2, (floorDeep + waterLine - (p.Dd - p.Ds) * s) * 0.5, 0);
            AddAlignedDim(tr, btr, dDd1, dDd2, dDdL, ld);

            // بعد سمك الجدار
            Point3d dT1 = new Point3d(ox, wallTop + 0.1 * s, 0);
            Point3d dT2 = new Point3d(xLeftWall, wallTop + 0.1 * s, 0);
            Point3d dTL = new Point3d((ox + xLeftWall) * 0.5, wallTop + 0.15 * s, 0);
            AddAlignedDim(tr, btr, dT1, dT2, dTL, ld);

            // نصوص تسمية
            AddText(tr, btr, "قطاع A-A", new Point3d(ox, wallTop + 0.5 * s, 0), textH * 1.3, lt);
            AddText(tr, btr, "Freeboard", new Point3d(ox - 0.5 * s, waterLine + 0.02 * s, 0), textH * 0.7, lt);
            AddText(tr, btr, "م", new Point3d(extRight + 0.1 * s, wallTop + 0.1 * s, 0), textH * 0.6, lt);
        }

        // ─────────── 3. القطاع العرضي B-B ───────────
        private void DrawCrossSection(Transaction tr, BlockTableRecord btr, PoolParams p, double s,
            ObjectId ls, ObjectId lw, ObjectId lh, ObjectId ld, ObjectId lt)
        {
            double ox = (p.L + 30.0 / s) * s; // L*s + 30
            double oy = 100 * s;
            double textH = 0.08 * s;
            double dimOff = 0.3 * s;

            // العرض بالمقياس
            double wScaled = p.W * s;
            double halfW = wScaled * 0.5;
            double centerX = ox + halfW;

            // ارتفاع القطاع (نأخذ أعماق الضحل)
            double wallTop     = oy + (p.Freeboard + p.WaterLevel) * s;
            double waterLine   = oy + p.Freeboard * s;
            double floorLevel  = oy;

            double xLeftInner  = centerX - halfW + p.t * s;
            double xRightInner = centerX + halfW - p.t * s;

            double xLeftOuter  = centerX - halfW;
            double xRightOuter = centerX + halfW;

            // السطح الداخلي
            Point2dCollection innerPts = new Point2dCollection
            {
                new Point2d(xLeftInner, wallTop),
                new Point2d(xLeftInner, floorLevel),
                new Point2d(xRightInner, floorLevel),
                new Point2d(xRightInner, wallTop)
            };
            AddLWPolyline(tr, btr, innerPts, ls, true);

            // السطح الخارجي
            Point2dCollection outerPts = new Point2dCollection
            {
                new Point2d(xLeftOuter, wallTop),
                new Point2d(xLeftOuter, floorLevel - p.tf * s),
                new Point2d(xRightOuter, floorLevel - p.tf * s),
                new Point2d(xRightOuter, wallTop)
            };
            ObjectId outerPolyIdB = AddLWPolyline(tr, btr, outerPts, ls, true);

            // خط سطح الأرض
            double gYb = floorLevel - p.tf * s - 0.2 * s;
            AddLine(tr, btr, new Point3d(xLeftOuter - 0.2 * s, gYb, 0),
                           new Point3d(xRightOuter + 0.2 * s, gYb, 0), ls);

            // منطقة المياه
            Point2dCollection waterPtsB = new Point2dCollection
            {
                new Point2d(xLeftInner, waterLine),
                new Point2d(xRightInner, waterLine),
                new Point2d(xRightInner, floorLevel + 0.01),
                new Point2d(xLeftInner, floorLevel + 0.01)
            };
            ObjectId waterPolyIdB = AddLWPolyline(tr, btr, waterPtsB, lw, true);
            ObjectIdCollection waterBoundB = new ObjectIdCollection { waterPolyIdB };
            AddHatch(tr, btr, "ANGLE", 0.5 * s, waterBoundB, lw);

            // أبعاد الجدران
            // الجدار الأيسر
            Point3d dWL1 = new Point3d(xLeftOuter, floorLevel, 0);
            Point3d dWL2 = new Point3d(xLeftOuter, wallTop, 0);
            Point3d dWLL = new Point3d(xLeftOuter - dimOff, (floorLevel + wallTop) * 0.5, 0);
            AddAlignedDim(tr, btr, dWL1, dWL2, dWLL, ld);

            // بعد العرض
            Point3d dW1 = new Point3d(xLeftOuter, wallTop + 0.2 * s, 0);
            Point3d dW2 = new Point3d(xRightOuter, wallTop + 0.2 * s, 0);
            Point3d dWL = new Point3d(centerX, wallTop + 0.35 * s, 0);
            AddAlignedDim(tr, btr, dW1, dW2, dWL, ld);

            // نصوص
            AddText(tr, btr, "قطاع B-B", new Point3d(ox, wallTop + 0.5 * s, 0), textH * 1.3, lt);
            AddText(tr, btr, $"W = {p.W} م", new Point3d(ox + 0.1 * s, wallTop + 0.1 * s, 0), textH, lt);
        }

        // ─────────── 4. التفصيل A — الجدار والأرضية ───────────
        private void DrawDetailA(Transaction tr, BlockTableRecord btr, PoolParams p, double s,
            ObjectId ls, ObjectId lh, ObjectId ld, ObjectId lt)
        {
            double ox = (p.L + 30.0 / s) * s;
            double oy = 0;
            double textH = 0.08 * s;
            double detailScale = 2.0; // تكبير 2x

            // نقطة التقاء الجدار بالأرضية (زاوية داخلية)
            double refX = ox;
            double refY = oy + 1.0 * s; // هامش فوق

            // الجدار (يمين)
            double wallThick = p.t * s * detailScale;
            double floorThick = p.tf * s * detailScale;
            double viewWidth = 1.2 * s * detailScale;
            double viewHeight = 1.2 * s * detailScale;

            // — خط الجدار (الجزء الداخلي من المسبح) —
            // الجدار الخارجي
            double jX_outer = refX + floorThick;
            double jX_inner = refX + floorThick + wallThick;
            double floorTop = refY;
            double floorBottom = refY - floorThick;
            double wallTopY = refY + viewHeight;

            // الجدار — خط خارجي
            Point2dCollection wallPoly = new Point2dCollection
            {
                new Point2d(jX_outer, floorTop),
                new Point2d(jX_outer, wallTopY),
                new Point2d(jX_inner, wallTopY),
                new Point2d(jX_inner, floorTop)
            };
            ObjectId wallPolyId = AddLWPolyline(tr, btr, wallPoly, ls, true);

            // الجدار — خط داخلي (امتداد للأرضية)
            Point2dCollection floorPoly = new Point2dCollection
            {
                new Point2d(refX, floorTop),
                new Point2d(refX, floorBottom),
                new Point2d(jX_inner, floorBottom),
                new Point2d(jX_inner, floorTop)
            };
            ObjectId floorPolyId = AddLWPolyline(tr, btr, floorPoly, ls, true);

            // حشوة خرسانة للجدار
            ObjectIdCollection wallBound = new ObjectIdCollection { wallPolyId };
            AddHatch(tr, btr, "ANSI31", 0.3 * s * detailScale, wallBound, lh);

            // حشوة خرسانة للأرضية
            ObjectIdCollection floorBound = new ObjectIdCollection { floorPolyId };
            AddHatch(tr, btr, "ANSI31", 0.3 * s * detailScale, floorBound, lh);

            // — تسليح رمزي —
            // أسياخ أفقية في الجدار (دوائر بخطوط)
            double rebarSpacing = 0.1 * s * detailScale;
            double rebarR = 0.01 * s * detailScale;
            for (double y = floorTop + rebarSpacing; y < wallTopY - rebarSpacing * 0.5; y += rebarSpacing)
            {
                double cx = (jX_outer + jX_inner) * 0.5;
                AddCircle(tr, btr, new Point3d(cx, y, 0), rebarR, ls);
            }

            // أسياخ رأسية في الأرضية
            for (double x = refX + rebarSpacing; x < jX_inner - rebarSpacing * 0.5; x += rebarSpacing)
            {
                double cy = (floorTop + floorBottom) * 0.5;
                AddCircle(tr, btr, new Point3d(x, cy, 0), rebarR, ls);
            }

            // أبعاد
            double dimOffset = 0.15 * s * detailScale;

            // بعد سمك الجدار
            Point3d dT1 = new Point3d(jX_outer, wallTopY + 0.05 * s * detailScale, 0);
            Point3d dT2 = new Point3d(jX_inner, wallTopY + 0.05 * s * detailScale, 0);
            Point3d dTL = new Point3d((jX_outer + jX_inner) * 0.5, wallTopY + 0.1 * s * detailScale, 0);
            AddAlignedDim(tr, btr, dT1, dT2, dTL, ld);

            // بعد سمك الأرضية
            Point3d dF1 = new Point3d(refX - dimOffset, floorTop, 0);
            Point3d dF2 = new Point3d(refX - dimOffset, floorBottom, 0);
            Point3d dFL = new Point3d(refX - dimOffset * 2, (floorTop + floorBottom) * 0.5, 0);
            AddAlignedDim(tr, btr, dF1, dF2, dFL, ld);

            // نصوص
            AddText(tr, btr, "تفصيل A - الجدار والأرضية", new Point3d(refX, wallTopY + 0.25 * s * detailScale, 0), textH * 1.2, lt);
            AddText(tr, btr, $"S = 1:{p.Scale / detailScale:F0}", new Point3d(refX + 0.5 * s * detailScale, wallTopY + 0.25 * s * detailScale, 0), textH * 0.8, lt);
            AddText(tr, btr, "خرسانة", new Point3d(jX_outer + 0.02 * s * detailScale, wallTopY * 0.5, 0), textH * 0.6, lt);
            AddText(tr, btr, "تسليح", new Point3d((jX_outer + jX_inner) * 0.5 + 0.03 * s * detailScale, wallTopY * 0.7, 0), textH * 0.6, lt);
        }

        // ─────────── 5. التفصيل B — Skimmer / Overflow ───────────
        private void DrawDetailB(Transaction tr, BlockTableRecord btr, PoolParams p, double s,
            ObjectId ls, ObjectId lp, ObjectId lw, ObjectId lh, ObjectId ld, ObjectId lt)
        {
            double ox = (p.L + 30.0 / s) * s;
            double oy = 0;
            double textH = 0.08 * s;
            double detailScale = 2.0;
            double yOff = 1.5 * s * detailScale; // هامش لتفصيل A

            double refX = ox;
            double refY = oy + yOff;

            if (p.PoolType == "S")
            {
                // ─── تفصيل Skimmer ───
                double skW = 0.3 * s * detailScale;
                double skH = 0.25 * s * detailScale;
                double wallThick = p.t * s * detailScale;
                double skX = refX + wallThick * 0.5;
                double skY = refY;

                // جدار المسبح (جزء صغير)
                Point2dCollection wallRect = new Point2dCollection
                {
                    new Point2d(refX, skY),
                    new Point2d(refX, skY + skH * 1.8),
                    new Point2d(refX + wallThick, skY + skH * 1.8),
                    new Point2d(refX + wallThick, skY)
                };
                ObjectId wallRectId = AddLWPolyline(tr, btr, wallRect, ls, true);
                ObjectIdCollection wallBound = new ObjectIdCollection { wallRectId };
                AddHatch(tr, btr, "ANSI31", 0.3 * s * detailScale, wallBound, lh);

                // skimmer box
                double skBoxLeft = refX + wallThick + 0.02 * s * detailScale;
                Point2dCollection skBox = new Point2dCollection
                {
                    new Point2d(skBoxLeft, skY + skH * 0.4),
                    new Point2d(skBoxLeft + skW, skY + skH * 0.4),
                    new Point2d(skBoxLeft + skW, skY + skH * 1.4),
                    new Point2d(skBoxLeft, skY + skH * 1.4)
                };
                AddLWPolyline(tr, btr, skBox, lp, true);

                // فتحة الجدار للـ skimmer
                AddLine(tr, btr, new Point3d(refX, skY + skH * 0.6, 0),
                               new Point3d(skBoxLeft, skY + skH * 0.6, 0), lp);
                AddLine(tr, btr, new Point3d(refX, skY + skH * 1.2, 0),
                               new Point3d(skBoxLeft, skY + skH * 1.2, 0), lp);

                // سهم تدفق
                double arrowY = (skY + skH * 0.6 + skY + skH * 1.2) * 0.5;
                double arrowX = skBoxLeft + skW + 0.05 * s * detailScale;
                AddLine(tr, btr, new Point3d(refX - 0.05 * s * detailScale, arrowY, 0),
                               new Point3d(arrowX + 0.1 * s * detailScale, arrowY, 0), lp);
                // سهم
                AddLine(tr, btr, new Point3d(arrowX + 0.1 * s * detailScale, arrowY, 0),
                               new Point3d(arrowX + 0.05 * s * detailScale, arrowY + 0.02 * s * detailScale, 0), lp);
                AddLine(tr, btr, new Point3d(arrowX + 0.1 * s * detailScale, arrowY, 0),
                               new Point3d(arrowX + 0.05 * s * detailScale, arrowY - 0.02 * s * detailScale, 0), lp);

                // خط الماء
                AddLine(tr, btr, new Point3d(refX - 0.05 * s * detailScale, skY + skH * 0.6, 0),
                               new Point3d(skBoxLeft + skW + 0.1 * s * detailScale, skY + skH * 0.6, 0), lp);

                AddText(tr, btr, "Skimmer", new Point3d(skBoxLeft - 0.02 * s * detailScale, skY + skH * 1.7, 0), textH * 0.7, lt);
                AddText(tr, btr, "تفصيل B - Skimmer", new Point3d(refX - 0.05 * s * detailScale, skY + skH * 2.2, 0), textH * 1.1, lt);
            }
            else
            {
                // ─── تفصيل Overflow ───
                double wallThick = p.t * s * detailScale;
                double channelW = 0.15 * s * detailScale;
                double channelH = 0.15 * s * detailScale;
                double totalH = 0.4 * s * detailScale;

                // جدار المسبح
                Point2dCollection wallRect = new Point2dCollection
                {
                    new Point2d(refX, refY),
                    new Point2d(refX, refY + totalH),
                    new Point2d(refX + wallThick, refY + totalH),
                    new Point2d(refX + wallThick, refY)
                };
                ObjectId wallRectId = AddLWPolyline(tr, btr, wallRect, ls, true);
                ObjectIdCollection wallBound = new ObjectIdCollection { wallRectId };
                AddHatch(tr, btr, "ANSI31", 0.3 * s * detailScale, wallBound, lh);

                // قناة Overflow (gutter) فوق الجدار
                double gutterTop = refY + totalH;
                double gutterBottom = refY + totalH - channelH;
                Point2dCollection gutter = new Point2dCollection
                {
                    new Point2d(refX - channelW, gutterTop),
                    new Point2d(refX - channelW, gutterBottom),
                    new Point2d(refX + wallThick + channelW, gutterBottom),
                    new Point2d(refX + wallThick + channelW, gutterTop)
                };
                AddLWPolyline(tr, btr, gutter, lp, true);

                // خط الماء
                double waterY = gutterBottom + 0.02 * s * detailScale;
                AddLine(tr, btr, new Point3d(refX - channelW - 0.05 * s * detailScale, waterY, 0),
                               new Point3d(refX + wallThick + channelW + 0.05 * s * detailScale, waterY, 0), lw);

                // اتجاه overflow
                double ovArrowY = (gutterBottom + gutterTop) * 0.5;
                double ovArrowX = refX + wallThick + channelW + 0.08 * s * detailScale;
                AddLine(tr, btr, new Point3d(refX - channelW - 0.08 * s * detailScale, ovArrowY, 0),
                               new Point3d(ovArrowX + 0.1 * s * detailScale, ovArrowY, 0), lp);
                AddLine(tr, btr, new Point3d(ovArrowX + 0.1 * s * detailScale, ovArrowY, 0),
                               new Point3d(ovArrowX + 0.05 * s * detailScale, ovArrowY + 0.02 * s * detailScale, 0), lp);
                AddLine(tr, btr, new Point3d(ovArrowX + 0.1 * s * detailScale, ovArrowY, 0),
                               new Point3d(ovArrowX + 0.05 * s * detailScale, ovArrowY - 0.02 * s * detailScale, 0), lp);

                AddText(tr, btr, "Overflow Gutter", new Point3d(refX, refY + totalH + 0.07 * s * detailScale, 0), textH * 0.7, lt);
                AddText(tr, btr, "تفصيل B - Overflow", new Point3d(refX - 0.05 * s * detailScale, refY + totalH + 0.2 * s * detailScale, 0), textH * 1.1, lt);
            }
        }

        // ═══════════════════ الطبقات ═══════════════════
        private ObjectId CreateLayer(Transaction tr, Database db, string name, short colorIndex)
        {
            LayerTable lt = tr.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;
            if (lt.Has(name))
                return lt[name];

            lt.UpgradeOpen();
            LayerTableRecord ltr = new LayerTableRecord
            {
                Name = name,
                Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, colorIndex)
            };
            lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
            return ltr.ObjectId;
        }

        private ObjectId GetLayerId(Transaction tr, Database db, string name)
        {
            LayerTable lt = tr.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;
            if (lt.Has(name))
                return lt[name];
            return ObjectId.Null;
        }

        // ═══════════════════ دوال الرسم المساعدة ═══════════════════

        private ObjectId AddLine(Transaction tr, BlockTableRecord btr,
            Point3d p1, Point3d p2, ObjectId layerId)
        {
            Line line = new Line(p1, p2) { LayerId = layerId };
            btr.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);
            return line.ObjectId;
        }

        private ObjectId AddLWPolyline(Transaction tr, BlockTableRecord btr,
            Point2dCollection pts, ObjectId layerId, bool closed = false)
        {
            Polyline pline = new Polyline();
            pline.LayerId = layerId;
            for (int i = 0; i < pts.Count; i++)
                pline.AddVertexAt(i, pts[i], 0, 0, 0);
            if (closed)
                pline.Closed = true;
            btr.AppendEntity(pline);
            tr.AddNewlyCreatedDBObject(pline, true);
            return pline.ObjectId;
        }

        private ObjectId AddCircle(Transaction tr, BlockTableRecord btr,
            Point3d center, double radius, ObjectId layerId)
        {
            Circle circle = new Circle(center, Vector3d.ZAxis, radius)
            {
                LayerId = layerId
            };
            btr.AppendEntity(circle);
            tr.AddNewlyCreatedDBObject(circle, true);
            return circle.ObjectId;
        }

        private ObjectId AddText(Transaction tr, BlockTableRecord btr,
            string text, Point3d position, double height, ObjectId layerId)
        {
            DBText dbText = new DBText
            {
                TextString = text,
                Position = position,
                Height = height,
                LayerId = layerId
            };
            btr.AppendEntity(dbText);
            tr.AddNewlyCreatedDBObject(dbText, true);
            return dbText.ObjectId;
        }

        private ObjectId AddAlignedDim(Transaction tr, BlockTableRecord btr,
            Point3d p1, Point3d p2, Point3d dimLinePoint, ObjectId layerId)
        {
            AlignedDimension dim = new AlignedDimension
            {
                XLine1Point = p1,
                XLine2Point = p2,
                DimLinePoint = dimLinePoint,
                LayerId = layerId
            };
            btr.AppendEntity(dim);
            tr.AddNewlyCreatedDBObject(dim, true);
            return dim.ObjectId;
        }

        private ObjectId AddHatch(Transaction tr, BlockTableRecord btr,
            string patternName, double patternScale,
            ObjectIdCollection boundaries, ObjectId layerId)
        {
            Hatch hatch = new Hatch();
            hatch.LayerId = layerId;
            btr.AppendEntity(hatch);
            tr.AddNewlyCreatedDBObject(hatch, true);

            hatch.SetDatabaseDefaults();
            hatch.SetHatchPattern(HatchPatternType.PreDefined, patternName);
            hatch.PatternScale = patternScale;

            hatch.AppendLoop(HatchLoopTypes.Default, boundaries);
            hatch.EvaluateHatch(true);
            return hatch.ObjectId;
        }
    }
}
