using System;
using System.Collections.Generic;
using SkiaSharp;
using g3;
using gs;

namespace SliceViewer 
{
	class SliceViewCanvas : PanZoomCanvas2D
	{
        public bool ShowPathStartPoints = true;
		public bool ShowOpenEndpoints = true;
        public bool ShowAllPathPoints = false;
        public bool ShowTravels = true;
		public bool ShowDepositMoves = true;
        public bool ShowFillArea = false;

		public bool ShowIssues = false;

        public float PathDiameterMM = 0.4f;


		public enum NumberModes
		{
			NoNumbers,
			PathNumbers
		}
		NumberModes num_mode = NumberModes.NoNumbers;
		public NumberModes NumberMode {
			get { return num_mode; }
			set { num_mode = value; QueueDraw(); }
		}


		bool show_below = true;
		public bool ShowBelowLayer {
			get { return show_below; }
			set { show_below = value; QueueDraw(); }
		}


		public SliceViewCanvas() 
		{
			SetPaths(new PathSet());
			InitializeInternals();
		}

		PathSet Paths;
		LayersDetector Layers;
		int currentLayer = 0;
		Func<Vector3d, byte> LayerFilterF = (v) => { return 255; };

		public void SetPaths(PathSet paths) {
			Paths = paths;
			Layers = new LayersDetector(Paths);
			CurrentLayer = 0;
        }

		public int CurrentLayer {
			get { return currentLayer; }
			set {
				currentLayer = MathUtil.Clamp(value, 0, Layers.Layers - 1);
				Interval1d layer_zrange = Layers.GetLayerZInterval(currentLayer);
				Interval1d prev_zrange = Layers.GetLayerZInterval(currentLayer - 1);
				LayerFilterF = (v) => {
					if (layer_zrange.Contains(v.z))
						return 255;
					else if (show_below && prev_zrange.Contains(v.z))
						return 16;
					return (byte)0;
				};
                invalidate_path_caches();
                QueueDraw();
			}
		}



		    



        Func<Vector2d, Vector2f> SceneXFormF = (v) => { return (Vector2f)v; };
		Func<Vector2d, SKPoint> SceneToSkiaF = null;
		void InitializeInternals()
		{
			SceneToSkiaF = (v) => {
				Vector2f p = SceneXFormF(v);
				return new SKPoint(p.x, p.y);
			};
		}



		protected override AxisAlignedBox2d DrawingBounds {
			get {
				AxisAlignedBox3d bounds3 = Paths.ExtrudeBounds;
				AxisAlignedBox2d bounds = (bounds3 == AxisAlignedBox3d.Empty) ?
					new AxisAlignedBox2d(0, 0, 500, 500) :
					new AxisAlignedBox2d(bounds3.Min.x, bounds3.Min.y, bounds3.Max.x, bounds3.Max.y);
				return bounds;
			}
		}


        float dimensionScale = 1.0f;

		protected override void DrawScene(SKCanvas canvas, Func<Vector2d, Vector2f> ViewTransformF)
		{
			Random jitter = new Random(313377);
			Func<Vector2d> jitterF = () => 
				{ return Vector2d.Zero; };
				//{ return 0.1 * new Vector2d(jitter.NextDouble(), jitter.NextDouble()); };

			canvas.Clear(SkiaUtil.Color(255, 255, 255, 255));

			// update scene xform
			SceneXFormF = (pOrig) => {
				pOrig += jitterF();
				return ViewTransformF(pOrig);
			};

            // figure out dimension scaling factor
            Vector2f a = SceneXFormF(Vector2d.Zero), b = SceneXFormF(Vector2d.AxisX);
            dimensionScale = (b.x - a.x);

			using (var paint = new SKPaint())
			{
				paint.IsAntialias = true;

				paint.StrokeWidth = 1;
                paint.Style = SKPaintStyle.Stroke;

				if ( ShowDepositMoves )
					DrawLayerPaths(Paths, canvas, paint);

                if (ShowFillArea) {
					//DrawFill(Paths, canvas); 
					DrawFillOverlaps(Paths, canvas);
					//DrawFillOverlapsIntegrate(Paths, canvas);
                }

                if (ShowAllPathPoints)
                    DrawLayerPoints(Paths, canvas, paint);

                if (ShowIssues) {
                    validate_path_caches();
                    if ( CurrentLayer > 0 )
                        MarkFloatingEndpointsAndCorners(Paths, canvas, paint);
                }

				if (NumberMode == NumberModes.PathNumbers )
					DrawPathLabels(Paths, canvas, paint);

                DrawLayerInfo(canvas, paint);
            }
        }








		private void DrawLayerPaths(PathSet pathSetIn, SKCanvas canvas, SKPaint paint) {

			SKColor extrudeColor = SkiaUtil.Color(0, 0, 0, 255);
			SKColor travelColor = SkiaUtil.Color(0, 255, 0, 128);
			SKColor startColor = SkiaUtil.Color(255, 0, 0, 128);
			SKColor planeColor = SkiaUtil.Color(0, 0, 255, 128);
			float pointR = 3f;

			Action<LinearPath3<PathVertex>> drawPath3F = (polyPath) => {

				Vector3d v0 = polyPath.Start.Position;
				byte layer_alpha = LayerFilterF(v0);
				if (layer_alpha == 0)
					return;
				bool is_below = (layer_alpha < 255);

				SKPath path = MakePath(polyPath, SceneToSkiaF);
				if (polyPath.Type == PathTypes.Deposition) {
					paint.Color = extrudeColor;
					paint.StrokeWidth = 1.5f;
				} else if (polyPath.Type == PathTypes.Travel) {
					if (is_below)
						return;
                    if (ShowTravels == false)
                        return;
					paint.Color = travelColor;
					paint.StrokeWidth = 3;
				} else if (polyPath.Type == PathTypes.PlaneChange) {
					if (is_below)
						return;
                    if (ShowTravels == false)
                        return;
                    paint.StrokeWidth = 0.5f;
					paint.Color = planeColor;
				} else {
					if (is_below)
						return;					
					paint.Color = startColor;
				}
				paint.Color = SkiaUtil.Color(paint.Color, layer_alpha);

				if (is_below)
					paint.StrokeWidth = 6;
				canvas.DrawPath(path, paint);

				paint.StrokeWidth = 1;

				if (is_below == false && ShowPathStartPoints) {
					Vector2f pt = SceneXFormF(polyPath.Start.Position.xy);
					if (polyPath.Type == PathTypes.Deposition) {
						canvas.DrawCircle(pt.x, pt.y, pointR, paint);
					} else if (polyPath.Type == PathTypes.Travel) {
						canvas.DrawCircle(pt.x, pt.y, pointR, paint);
					} else if (polyPath.Type == PathTypes.PlaneChange) {
						paint.Style = SKPaintStyle.Fill;
						canvas.DrawCircle(pt.x, pt.y, 4f, paint);
						paint.Style = SKPaintStyle.Stroke;
					}
				}
			};

			ProcessLinearPaths(pathSetIn, drawPath3F);
		}








        private void DrawLayerPoints(PathSet pathSetIn, SKCanvas canvas, SKPaint paint)
        {

            SKColor pointColor = SkiaUtil.Color(0, 0, 0, 255);
            float pointR = 1.5f;

            Action<LinearPath3<PathVertex>> drawPathPoints = (polyPath) => {
                if (LayerFilterF(polyPath.Start.Position) < 255)
                    return;
                paint.Color = SkiaUtil.Color(pointColor, 255);
                paint.StrokeWidth = 1;
                for ( int vi = 0; vi < polyPath.VertexCount; vi++ ) {
                    Vector2f pt = (Vector2f)SceneXFormF(polyPath[vi].Position.xy);
                    paint.Style = SKPaintStyle.Fill;
                    canvas.DrawCircle(pt.x, pt.y, pointR, paint);
                }
            };

            ProcessLinearPaths(pathSetIn, drawPathPoints);
        }








        private void DrawFill(PathSet pathSetIn, SKCanvas baseCanvas)
        {
            SKColor fillColor = SkiaUtil.Color(255, 0, 255, 255);

            SKRect bounds = baseCanvas.ClipBounds;

            SKBitmap blitBitmap = new SKBitmap(PixelDimensions.x, PixelDimensions.y, SkiaUtil.ColorType(), SKAlphaType.Premul);
            IntPtr len;
            using (var skSurface = SKSurface.Create(blitBitmap.Info.Width, blitBitmap.Info.Height, SkiaUtil.ColorType(), SKAlphaType.Premul, blitBitmap.GetPixels(out len), blitBitmap.Info.RowBytes)) {
                var canvas = skSurface.Canvas;

				canvas.Clear(SkiaUtil.Color(255, 255, 255, 255));

                using (var paint = new SKPaint()) {
                    paint.IsAntialias = true;
                    paint.StrokeWidth = dimensionScale * PathDiameterMM;
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeCap = SKStrokeCap.Round;
                    paint.StrokeJoin = SKStrokeJoin.Round;
                    paint.Color = fillColor;

                    Action<LinearPath3<PathVertex>> drawPath3F = (polyPath) => {
                        if (polyPath.Type != PathTypes.Deposition)
                            return;
                        Vector3d v0 = polyPath.Start.Position;
                        byte layer_alpha = LayerFilterF(v0);
                        if (layer_alpha != 255)
                            return;
                        SKPath path = MakePath(polyPath, SceneToSkiaF);
                        canvas.DrawPath(path, paint);
                    };

                    ProcessLinearPaths(pathSetIn, drawPath3F);
                }
            }


            SKPaint blitPaint = new SKPaint();
            blitPaint.IsAntialias = false;
            blitPaint.BlendMode = SKBlendMode.SrcOver;
            blitPaint.Color = SkiaUtil.Color(0, 0, 0, 64);

            baseCanvas.DrawBitmap(blitBitmap, 0, 0, blitPaint);

            blitPaint.Dispose();
            blitBitmap.Dispose();
        }





        /// <summary>
        /// Point of this function is to be same as DrawFill (ie draw 'tubes') but to draw
        /// in such a way that overlap regions are highlighted. However it does not work yet,
        /// need to draw continuous SKPaths as much as possible but break at direction changes.
        /// </summary>
        private void DrawFillOverlaps(PathSet pathSetIn, SKCanvas baseCanvas)
        {
            SKColor fillColor = SkiaUtil.Color(255, 0, 255, 64);

            using (var paint = new SKPaint()) {
                paint.IsAntialias = true;
                paint.StrokeWidth = dimensionScale * PathDiameterMM;
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeCap = SKStrokeCap.Round;
                paint.StrokeJoin = SKStrokeJoin.Round;
                paint.Color = fillColor;

                Action<LinearPath3<PathVertex>> drawPath3F = (polyPath) => {
                    if (polyPath.Type != PathTypes.Deposition)
                        return;
                    Vector3d v0 = polyPath.Start.Position;
                    byte layer_alpha = LayerFilterF(v0);
                    if (layer_alpha != 255)
                        return;

					// draw each segment separately. results in lots of duplicate-circles, no good
					//for (int i = 1; i < polyPath.VertexCount; i++) {
					//    SKPoint a = SceneToSkiaF(polyPath[i - 1].Position.xy);
					//    SKPoint b = SceneToSkiaF(polyPath[i].Position.xy);
					//    baseCanvas.DrawLine(a.X, a.Y, b.X, b.Y, paint);
					//}

					// draw full path in one shot. Only shows overlaps between separate paths.
					//SKPath path = MakePath(polyPath, SceneToSkiaF);
					//baseCanvas.DrawPath(path, paint);

					// draw w/ angle threshold
					List<SKPath> paths = MakePathSegments(polyPath, SceneToSkiaF, 45);
					foreach (var path in paths)
						baseCanvas.DrawPath(path, paint);
                };

                ProcessLinearPaths(pathSetIn, drawPath3F);
            }
        }




		/// <summary>
		/// Point of this function is to be same as DrawFill (ie draw 'tubes') but to draw
		/// in such a way that overlap regions are highlighted. However it does not work yet,
		/// need to draw continuous SKPaths as much as possible but break at direction changes.
		/// </summary>
		private void DrawFillOverlapsIntegrate(PathSet pathSetIn, SKCanvas baseCanvas)
		{
			SKColor fillColor = SkiaUtil.Color(255, 0, 255, 5);
			float path_diam = dimensionScale * PathDiameterMM;
			float spot_r = path_diam * 0.5f;
			float spacing = 1.0f / dimensionScale;

			using (var paint = new SKPaint()) {
				paint.IsAntialias = true;
				paint.StrokeWidth = 1.0f;
				paint.Style = SKPaintStyle.Fill;
				paint.Color = fillColor;

				Action<LinearPath3<PathVertex>> drawPath3F = (polyPath) => {
					if (polyPath.Type != PathTypes.Deposition)
						return;
					Vector3d v0 = polyPath.Start.Position;
					byte layer_alpha = LayerFilterF(v0);
					if (layer_alpha != 255)
						return;

					// draw each segment separately. results in lots of duplicate-circles, no good
					for (int i = 1; i < polyPath.VertexCount; i++) {
						Vector2d a = polyPath[i - 1].Position.xy;
						Vector2d b = polyPath[i].Position.xy;
						double len = a.Distance(b);
						int n = (int)(len / spacing) + 1;
						int stop = (i == polyPath.VertexCount - 1) ? n : n - 1;
						for (int k = 0; k <= stop; ++k) {
							double t = (double)k / (double)n;
							Vector2d p = Vector2d.Lerp(a, b, t);
							SKPoint pk = SceneToSkiaF(p);
							baseCanvas.DrawCircle(pk.X, pk.Y, spot_r, paint);
						}
					}
				};

				ProcessLinearPaths(pathSetIn, drawPath3F);
			}
		}




        private void DrawPathLabels(PathSet pathSetIn, SKCanvas canvas, SKPaint paint) 
		{
			int counter = 1;

			paint.Style = SKPaintStyle.StrokeAndFill;
			paint.IsAntialias = true;
			paint.Color = SKColors.Black;
			paint.StrokeWidth = 1;
			paint.TextSize = 15.0f;

			Action<LinearPath3<PathVertex>> drawLabelF = (polyPath) => {
				Vector3d v0 = polyPath.Start.Position;
				byte layer_alpha = LayerFilterF(v0);
				if (layer_alpha < 255)
					return;

				string label = string.Format("{0}", counter++);
				paint.Color = SKColors.Black;

				Vector3d vPos = v0;
				float shiftX = 0, shiftY = 0;
				if ( polyPath.Type == PathTypes.Travel ) {
					vPos = (polyPath.Start.Position + polyPath.End.Position) * 0.5;
				} else if ( polyPath.Type == PathTypes.PlaneChange ) {
					shiftY = paint.TextSize * 0.5f;
					//shiftX = -paint.TextSize * 0.5f;
					paint.Color = SKColors.DarkRed;
				}

				SKPoint pos = SceneToSkiaF(vPos.xy);
				canvas.DrawText(label, pos.X + shiftX, pos.Y + shiftY, paint);
			};

			ProcessLinearPaths(pathSetIn, drawLabelF);
		}



        private void DrawLayerInfo(SKCanvas canvas, SKPaint paint)
        {
            string text = "Layer " + currentLayer.ToString();
            paint.Color = SKColors.Black;
            paint.StrokeWidth = 1;
            canvas.DrawText(text, 10, 10, paint);
        }





        private void MarkFloatingEndpointsAndCorners(PathSet pathSetIn, SKCanvas canvas, SKPaint paint)
        {
            double FloatingStartDistThreshMM = PathDiameterMM * 0.6f;

            float FloatCircleRadius = MapDimension(PathDiameterMM*0.5f, SceneToSkiaF);

            paint.Style = SKPaintStyle.Stroke;
            paint.IsAntialias = true;
            paint.Color = SKColors.Black;
            paint.StrokeWidth = 4;

            SKColor start_color = SKColors.Red.WithAlpha(128);
            SKColor end_color = SKColors.Orange.WithAlpha(128);

            Action<LinearPath3<PathVertex>> drawFloatF = (polyPath) => {
                if (polyPath.Type != PathTypes.Deposition)
                    return;

                Vector3d v0 = polyPath.Start.Position;
                byte layer_alpha = LayerFilterF(v0);
                if (layer_alpha < 255)
                    return;

                Vector2d nearPt;
                for ( int k = 0; k < polyPath.VertexCount; ++k ) {
                    if ( k > 0 && k < polyPath.VertexCount-1 ) {
                        double angle = polyPath.PlanarAngleD(k);
                        if (angle > 179)
                            continue;
                    }
                    Vector2d v = polyPath[k].Position.xy;
                    double dist = find_below_nearest(v, FloatingStartDistThreshMM, out nearPt);
                    if ( dist == double.MaxValue ) {
                        paint.Color = start_color;
                        SKPoint c = SceneToSkiaF(v), n = SceneToSkiaF(nearPt);
                        canvas.DrawCircle(c.X, c.Y, FloatCircleRadius, paint);
                    }
                }


                //Vector2d nearPt;
                //double dist = find_below_nearest(v0.xy, 4*FloatingStartDistThreshMM, out nearPt);
                //if ( dist < double.MaxValue && dist > FloatingStartDistThreshMM) {
                //    paint.Color = start_color;
                //    SKPoint c = SceneToSkiaF(v0.xy), n = SceneToSkiaF(nearPt);
                //    canvas.DrawCircle(c.X, c.Y, FloatCircleRadius, paint);
                //    paint.Color = SKColors.DarkRed;
                //    canvas.DrawLine(c.X, c.Y, n.X, n.Y, paint);
                //}

                //Vector3d v1 = polyPath.End.Position;
                //double dist2 = find_below_nearest(v1.xy, 4*FloatingStartDistThreshMM, out nearPt);
                //if (dist2 < double.MaxValue && dist2 > FloatingStartDistThreshMM) {
                //    paint.Color = end_color;
                //    SKPoint c = SceneToSkiaF(v1.xy), n = SceneToSkiaF(nearPt);
                //    canvas.DrawCircle(c.X, c.Y, FloatCircleRadius, paint);
                //    paint.Color = SKColors.DarkOrange;
                //    canvas.DrawLine(c.X, c.Y, n.X, n.Y, paint);
                //}


                

            };

            ProcessLinearPaths(pathSetIn, drawFloatF);
        }







        bool path_cache_valid = false;
        SegmentHashGrid2d<Segment2d> below_grid;
        SegmentHashGrid2d<Segment2d> current_grid;

        double find_below_nearest(Vector2d pt, double distThresh, out Vector2d nearPt)
        {
            nearPt = Vector2d.MaxValue;
            var result =
                below_grid.FindNearestInSquaredRadius(pt, distThresh * distThresh,
                    (seg) => { return seg.DistanceSquared(pt); }, null);
            if (result.Value == double.MaxValue)
                return double.MaxValue;
            nearPt = result.Key.NearestPoint(pt);
            return Math.Sqrt(result.Value);
        }


        double find_current_nearest(Vector2d pt, double distThresh, out Vector2d nearPt)
        {
            nearPt = Vector2d.MaxValue;
            var result =
                current_grid.FindNearestInSquaredRadius(pt, distThresh * distThresh,
                    (seg) => { return seg.DistanceSquared(pt); }, null);
            if (result.Value != double.MaxValue) {
                nearPt = result.Key.NearestPoint(pt);
            }
            return Math.Sqrt(result.Value);
        }

        private void invalidate_path_caches()
        {
            path_cache_valid = false;
        }

        private void validate_path_caches()
        {
            if (path_cache_valid)
                return;

            double maxLen = 2.5f;
            double maxLenSqr = maxLen * maxLen;
            Segment2d invalid = new Segment2d(Vector2d.MaxValue, Vector2d.MaxValue);

            below_grid = new SegmentHashGrid2d<Segment2d>(3 * maxLen, invalid);
            current_grid = new SegmentHashGrid2d<Segment2d>(3 * maxLen, invalid);

            Action<LinearPath3<PathVertex>> pathFuncF = (polyPath) => {
                if (polyPath.Type != PathTypes.Deposition)
                    return;

                Vector3d v0 = polyPath.Start.Position;
                byte layer_alpha = LayerFilterF(v0);
                if (layer_alpha == 0)
                    return;
                bool is_below = (layer_alpha < 255);
                var grid = (is_below) ? below_grid : current_grid;

                int N = polyPath.VertexCount;
                for ( int k = 0; k < N-1; ++k) {
                    Vector2d a = polyPath[k].Position.xy;
                    Vector2d b = polyPath[k+1].Position.xy;
                    double d2 = a.DistanceSquared(b);
                    if ( d2 < maxLenSqr ) {
                        Segment2d s = new Segment2d(a, b);
                        grid.InsertSegment(s, s.Center, s.Extent);
                        continue;
                    }
                    int subdivs = (int)(d2 / maxLenSqr);
                    Vector2d prev = a;
                    for ( int i = 1; i <= subdivs; ++i ) {
                        double t = (double)i / (double)subdivs;
                        Vector2d next = Vector2d.Lerp(a, b, t);
                        Segment2d s = new Segment2d(prev, next);
                        grid.InsertSegment(s, s.Center, s.Extent);
                        prev = next;
                    }
                }
            };

            ProcessLinearPaths(Paths, pathFuncF);

            path_cache_valid = true;
        }







        // utility stuff that could go somewhere else...



        static void ProcessLinearPaths(PathSet pathSetIn, Action<LinearPath3<PathVertex>> processF)
        {
            Action<IPath> drawPath = (path) => {
                if (path is LinearPath3<PathVertex>)
                    processF(path as LinearPath3<PathVertex>);
                // else we might have other path type...
            };
            Action<IPathSet> drawPaths = null;
            drawPaths = (pathList) => {
                foreach (IPath path in pathList) {
                    if (path is IPathSet)
                        drawPaths(path as IPathSet);
                    else
                        drawPath(path);
                }
            };

            drawPaths(pathSetIn);
        }



        static float MapDimension(float dimension, Func<Vector2d, SKPoint> mapF)
        {
            SKPoint o = mapF(new Vector2d(0, 0));
            SKPoint x = mapF(new Vector2d(dimension, 0));
            float dx = x.X - o.X, dy = x.Y - o.Y;
            return (float)Math.Sqrt(dx*dx + dy*dy);
        }


        static SKPath MakePath<T>(LinearPath3<T> path, Func<Vector2d, SKPoint> mapF) where T : IPathVertex
        {
            SKPath p = new SKPath();
            p.MoveTo(mapF(path[0].Position.xy));
            for (int i = 1; i < path.VertexCount; i++)
                p.LineTo(mapF(path[i].Position.xy));
            return p;
        }

        static List<SKPath> MakePathSegments<T>(LinearPath3<T> path, Func<Vector2d, SKPoint> mapF, double angleThreshD) where T : IPathVertex
        {
            List<SKPath> result = new List<SKPath>();

            Vector2d prevprev = path[0].Position.xy;

            SKPath p = new SKPath();
            result.Add(p);
            p.MoveTo(mapF(prevprev));
            if (path.VertexCount == 1) {
                p.LineTo(mapF(prevprev));
                return result;
            }

            Vector2d prev = path[1].Position.xy;
            int i = 2;
            p.LineTo(mapF(prev));

            while (i < path.VertexCount) {
                Vector2d next = path[i++].Position.xy;
                double turnAngle = Vector2d.AngleD((prevprev - prev).Normalized, (next - prev).Normalized);
                if (turnAngle < angleThreshD) {
                    p = new SKPath();
                    result.Add(p);
                    p.MoveTo(mapF(prev));
                    p.LineTo(mapF(next));
                } else {
                    p.LineTo(mapF(next));
                }
                prevprev = prev;
                prev = next;
            }
            return result;
        }
















        public List<PolyLine2d> GetPolylinesForLayer(int layer)
        {
            PathSet pathSetIn = Paths;

            SKColor extrudeColor = SkiaUtil.Color(0, 0, 0, 255);
            Interval1d layer_zrange = Layers.GetLayerZInterval(layer);

            List<PolyLine2d> polylines = new List<PolyLine2d>();

            Action<LinearPath3<PathVertex>> drawPath3F = (polyPath) => {
                Vector3d v0 = polyPath.Start.Position;
                if (layer_zrange.Contains(v0.z) == false)
                    return;
                if (polyPath.Type != PathTypes.Deposition)
                    return;

                PolyLine2d pline = new PolyLine2d();
                for (int i = 0; i < polyPath.VertexCount; ++i)
                    pline.AppendVertex(polyPath[i].Position.xy);

                polylines.Add(pline);
            };

            ProcessLinearPaths(pathSetIn, drawPath3F);

            return polylines;
        }





    }





}
