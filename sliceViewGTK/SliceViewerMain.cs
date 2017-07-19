using System;
using System.Collections.Generic;
using System.IO;

using Gtk;
using GLib;
using g3;
using gs;

namespace SliceViewer
{


	class MainClass
	{

		public static Window MainWindow;
		public static SliceViewCanvas View; 


		public static void Main(string[] args)
		{
			ExceptionManager.UnhandledException += delegate (UnhandledExceptionArgs expArgs) {
				Console.WriteLine(expArgs.ExceptionObject.ToString());
				expArgs.ExitApplication = true;
			};

			Gtk.Application.Init();

			MainWindow = new Window("SliceViewer");
			MainWindow.SetDefaultSize(900, 600);
			MainWindow.SetPosition(WindowPosition.Center);
			MainWindow.DeleteEvent += delegate {
				Gtk.Application.Quit();
			};



			string sPath = "../../../sample_files/disc_single_layer.gcode";
			//string sPath = "../../../sample_files/disc_0p6mm.gcode";
			//string sPath = "../../../sample_files/square_linearfill.gcode";
			//string sPath = "../../../sample_files/thin_hex_test_part.gcode";
			//string sPath = "../../../sample_files/box_infill_50.gcode";
			//string sPath = "../../../sample_files/tube_adapter.gcode";
			//string sPath = "../../../sample_files/ring_2p2_makerbot.gcode";
			//string sPath = "/Users/rms/Desktop/print_experiment/cura_ring_2p2.gcode";
			//string sPath = "/Users/rms/Desktop/print_experiment/slic3r_ring_2p2.gcode";



#if true
			//GCodeFile genGCode = MakerbotTests.SimpleFillTest();
			//GCodeFile genGCode = MakerbotTests.SimpleShellsTest();
			//GCodeFile genGCode = MakerbotTests.InfillBoxTest();

			//GeneralPolygon2d poly = GetPolygonFromMesh("../../../sample_files/bunny_open.obj");
			//GCodeFile genGCode = MakerbotTests.ShellsPolygonTest(poly);
			//GCodeFile genGCode = MakerbotTests.StackedPolygonTest(poly, 2);
			//GCodeFile genGCode = MakerbotTests.StackedScaledPolygonTest(poly, 20, 0.5);

			//DMesh3 mesh = StandardMeshReader.ReadMesh("../../../sample_files/bunny_solid_2p5cm.obj");
			//DMesh3 mesh = StandardMeshReader.ReadMesh("../../../sample_files/bunny_solid_5cm_min.obj");
			//DMesh3 mesh = StandardMeshReader.ReadMesh("../../../sample_files/basic_step.obj");
			//DMesh3 mesh = StandardMeshReader.ReadMesh("../../../sample_files/slab_5deg.obj");
			//DMesh3 mesh = StandardMeshReader.ReadMesh("../../../sample_files/unsupported_slab_5deg.obj");
			//DMesh3 mesh = StandardMeshReader.ReadMesh("../../../sample_files/sphere_angles_1cm.obj");
			//DMesh3 mesh = StandardMeshReader.ReadMesh("../../../sample_files/inverted_cone_1.obj");
			//DMesh3 mesh = StandardMeshReader.ReadMesh("../../../sample_files/tube_adapter.obj");
			DMesh3 mesh = StandardMeshReader.ReadMesh("../../../sample_files/tube_1.obj");
			MeshUtil.ScaleMesh(mesh, Frame3f.Identity, 1.1f*Vector3f.One);
			GCodeFile genGCode = MakerbotTests.SliceMeshTest_Roofs(mesh);

			string sWritePath = "../../../sample_output/generated.gcode";
			StandardGCodeWriter writer = new StandardGCodeWriter();
			using ( StreamWriter w = new StreamWriter(sWritePath) ) {
				writer.WriteFile(genGCode, w);
			}
			sPath = sWritePath;
#endif


			GenericGCodeParser parser = new GenericGCodeParser();
			GCodeFile gcode;
			using (FileStream fs = new FileStream(sPath, FileMode.Open, FileAccess.Read)) {
				using (TextReader reader = new StreamReader(fs) ) {
					gcode = parser.Parse(reader);
				}
			}


			// write back out gcode we loaded
			//StandardGCodeWriter writer = new StandardGCodeWriter();
			//using ( StreamWriter w = new StreamWriter("../../../sample_output/writeback.gcode") ) {
			//	writer.WriteFile(gcode, w);
			//}

			GCodeToLayerPaths converter = new GCodeToLayerPaths();
			MakerbotInterpreter interpreter = new MakerbotInterpreter();
			interpreter.AddListener(converter);

			InterpretArgs interpArgs = new InterpretArgs();
			interpreter.Interpret(gcode, interpArgs);

			//MakerbotSettings settings = new MakerbotSettings();
			//CalculateExtrusion calc = new CalculateExtrusion(converter.Paths, settings);
			//calc.TestCalculation();

			PathSet Paths = converter.Paths;

            View = new SliceViewCanvas();
			View.SetPaths(Paths);
            MainWindow.Add(View);

			MainWindow.KeyReleaseEvent += Window_KeyReleaseEvent;

			// support drag-drop
			Gtk.TargetEntry[] target_table = new TargetEntry[] {
			  new TargetEntry ("text/uri-list", 0, 0),
			};
			Gtk.Drag.DestSet(MainWindow, DestDefaults.All, target_table, Gdk.DragAction.Copy);
			MainWindow.DragDataReceived += MainWindow_DragDataReceived;;


            MainWindow.ShowAll();

            Gtk.Application.Run();
        }


		static void LoadGCodeFile(string sPath) {
			GenericGCodeParser parser = new GenericGCodeParser();
			GCodeFile gcode;
			using (FileStream fs = new FileStream(sPath, FileMode.Open, FileAccess.Read)) {
				using (TextReader reader = new StreamReader(fs)) {
					gcode = parser.Parse(reader);
				}
			}

			GCodeToLayerPaths converter = new GCodeToLayerPaths();
			MakerbotInterpreter interpreter = new MakerbotInterpreter();
			interpreter.AddListener(converter);
			InterpretArgs interpArgs = new InterpretArgs();
			interpreter.Interpret(gcode, interpArgs);

			PathSet Paths = converter.Paths;
			View.SetPaths(Paths);		
		}


		void OnException(object o, UnhandledExceptionArgs args)
		{

		}


		private static void Window_KeyReleaseEvent(object sender, KeyReleaseEventArgs args)
		{
			if (args.Event.Key == Gdk.Key.Up) {
				if ((args.Event.State & Gdk.ModifierType.ShiftMask) != 0)
					View.CurrentLayer = View.CurrentLayer + 10;
				else
					View.CurrentLayer = View.CurrentLayer + 1;
			} else if (args.Event.Key == Gdk.Key.Down) {
				if ((args.Event.State & Gdk.ModifierType.ShiftMask) != 0)
					View.CurrentLayer = View.CurrentLayer - 10;
				else
					View.CurrentLayer = View.CurrentLayer - 1;

			} else if (args.Event.Key == Gdk.Key.n) {
				if (View.NumberMode == SliceViewCanvas.NumberModes.NoNumbers)
					View.NumberMode = SliceViewCanvas.NumberModes.PathNumbers;
				else
					View.NumberMode = SliceViewCanvas.NumberModes.NoNumbers;

			} else if (args.Event.Key == Gdk.Key.b) {
				View.ShowBelowLayer = !View.ShowBelowLayer;

			} else if ( args.Event.Key == Gdk.Key.q ) {
				SliceViewerTests.TestDGraph2();

			}
		}






		static void MainWindow_DragDataReceived(object o, DragDataReceivedArgs args)
		{
			string data = System.Text.Encoding.UTF8.GetString(args.SelectionData.Data);
			data = data.Trim('\r', '\n', '\0');
			if (Util.IsRunningOnMono()) {
				data = data.Replace("file://", "");
			} else {
				data = data.Replace("file:///", "");
			}
			data = data.Replace("%20", " ");        // gtk inserts these for spaces? maybe? wtf.
			try {
				LoadGCodeFile(data);
			} catch (Exception e) {
				using (var dialog = new Gtk.MessageDialog(MainWindow, DialogFlags.Modal, MessageType.Info, ButtonsType.Ok,
					"Exception loading {0} : {1}", data, e.Message)) {
					dialog.Show();
				}
			}
		}





		static GeneralPolygon2d GetPolygonFromMesh(string sPath) {
			DMesh3 mesh = StandardMeshReader.ReadMesh(sPath);
			MeshBoundaryLoops loops = new MeshBoundaryLoops(mesh);

			PlanarComplex complex = new PlanarComplex();

			foreach (var loop in loops ) {
				Polygon2d poly = new Polygon2d();
				DCurve3 curve = MeshUtil.ExtractLoopV(mesh, loop.Vertices);
				foreach (Vector3d v in curve.Vertices)
					poly.AppendVertex(v.xy);
				complex.AddPolygon(poly);
			}

			PlanarComplex.SolidRegionInfo solids = complex.FindSolidRegions(0.0, false);
			return solids.Polygons[0];
		}



	}
}
