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
        public bool ShowTravels = true;

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
				QueueDraw();
			}
		}



		    
		SKPath MakePath<T>(LinearPath3<T> path, Func<Vector2d, SKPoint> mapF) where T : IPathVertex
		{
			SKPath p = new SKPath();
			p.MoveTo(mapF(path[0].Position.xy));
			for ( int i = 1; i < path.VertexCount; i++ )
				p.LineTo( mapF(path[i].Position.xy) );
			return p;
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


			using (var paint = new SKPaint())
			{
				paint.IsAntialias = true;

				paint.StrokeWidth = 1;
                paint.Style = SKPaintStyle.Stroke;

				DrawLayerPaths(Paths, canvas, paint);

				if (NumberMode == NumberModes.PathNumbers )
					DrawPathLabels(Paths, canvas, paint);
			}
		}






		void ProcessLinearPaths(PathSet pathSetIn, Action<LinearPath3<PathVertex>> processF) {
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


	}





}
