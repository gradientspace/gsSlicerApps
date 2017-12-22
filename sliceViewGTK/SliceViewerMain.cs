using System;
using System.Collections.Generic;
using System.IO;

using Gtk;
using GLib;
using g3;
using gs;
using gs.info;

namespace SliceViewer
{


	class MainClass
	{

		public static Window MainWindow;
		public static SliceViewCanvas View;

		public static SingleMaterialFFFSettings LastSettings;


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

            DMesh3 readMesh = null;

#if true
            //GCodeFile genGCode = MakerbotTests.SimpleFillTest();
            //GCodeFile genGCode = MakerbotTests.SimpleShellsTest();
            //GCodeFile genGCode = MakerbotTests.InfillBoxTest();

            //GeneralPolygon2d poly = GetPolygonFromMesh("../../../sample_files/bunny_open.obj");
            //GCodeFile genGCode = MakerbotTests.ShellsPolygonTest(poly);
            //GCodeFile genGCode = MakerbotTests.StackedPolygonTest(poly, 2);
            //GCodeFile genGCode = MakerbotTests.StackedScaledPolygonTest(poly, 20, 0.5);

            //readMesh = StandardMeshReader.ReadMesh("../../../sample_files/bunny_solid_2p5cm.obj");
            //readMesh = StandardMeshReader.ReadMesh("../../../sample_files/bunny_solid_5cm_min.obj");
            //readMesh = StandardMeshReader.ReadMesh("../../../sample_files/basic_step.obj");
            //readMesh = StandardMeshReader.ReadMesh("../../../sample_files/slab_5deg.obj");
            //readMesh = StandardMeshReader.ReadMesh("../../../sample_files/unsupported_slab_5deg.obj");
            //readMesh = StandardMeshReader.ReadMesh("../../../sample_files/sphere_angles_1cm.obj");
            //readMesh = StandardMeshReader.ReadMesh("../../../sample_files/inverted_cone_1.obj");
            //readMesh = StandardMeshReader.ReadMesh("../../../sample_files/tube_adapter.obj");
            //readMesh = StandardMeshReader.ReadMesh("../../../sample_files/tube_1.obj");
            //readMesh = StandardMeshReader.ReadMesh("../../../sample_files/50x50x1_box.obj");
            //readMesh = StandardMeshReader.ReadMesh("../../../sample_files/crop_bracket.obj");
            //readMesh = StandardMeshReader.ReadMesh("../../../sample_files/thinwall2.obj");
            //readMesh = StandardMeshReader.ReadMesh("../../../sample_files/box_and_cylsheet.obj");
            //readMesh = StandardMeshReader.ReadMesh("../../../sample_files/box_and_opensheet.obj");
            //readMesh = StandardMeshReader.ReadMesh("../../../sample_files/radial_fins.obj");
            //readMesh = StandardMeshReader.ReadMesh("../../../sample_files/radial_fins_larger.obj");
            //readMesh = StandardMeshReader.ReadMesh("../../../sample_files/bunny_hollow_5cm.obj");
            //readMesh = StandardMeshReader.ReadMesh("../../../sample_files/notch_test_1.obj");
            //readMesh = StandardMeshReader.ReadMesh("../../../sample_files/variable_thins.obj");
            readMesh = StandardMeshReader.ReadMesh("../../../sample_files/arrow_posx.obj");
            //MeshUtil.ScaleMesh(readMesh, Frame3f.Identity, 1.1f*Vector3f.One);

            DMesh3[] meshComponents = MeshConnectedComponents.Separate(readMesh);
			//DMesh3[] meshComponents = new DMesh3[] { readMesh };

            PrintMeshAssembly meshes = new PrintMeshAssembly();
            meshes.AddMeshes(meshComponents);

            AxisAlignedBox3d bounds = meshes.TotalBounds;
            AxisAlignedBox2d bounds2 = new AxisAlignedBox2d(bounds.Center.xy, bounds.Width / 2, bounds.Height / 2);

#endif

            View = new SliceViewCanvas();


            bool TEST_SLS = false;
            PathSet ViewPaths = null;


            if (TEST_SLS) {

                // configure settings
                MakerbotSettings settings = new MakerbotSettings();
                settings.ClipSelfOverlaps = true;

                // slice meshes
                MeshPlanarSlicer slicer = new MeshPlanarSlicer() {
                    LayerHeightMM = settings.LayerHeightMM
                };
                slicer.Add(meshes);
                PlanarSliceStack slices = slicer.Compute();

                // run print generator
                GenericSLSPrintGenerator printGen = new GenericSLSPrintGenerator(
                    meshes, slices, settings
                );
                printGen.Generate();

                ViewPaths = printGen.Result;
                View.ShowOpenEndpoints = false;
                View.ShowTravels = false;
                View.ShowPathStartPoints = false;

            } else {

                if (readMesh != null) {
                    // generate gcode file for mesh
                    sPath = GenerateGCodeForMeshes(meshes);
                }

                // read gcode file
                GenericGCodeParser parser = new GenericGCodeParser();
                GCodeFile gcode;
                using (FileStream fs = new FileStream(sPath, FileMode.Open, FileAccess.Read)) {
                    using (TextReader reader = new StreamReader(fs)) {
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

                ViewPaths = converter.Paths;
            }


			View.SetPaths(ViewPaths);
			if (LastSettings != null)
				View.PathDiameterMM = (float)LastSettings.Machine.NozzleDiamMM;

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




        static string GenerateGCodeForMeshes(PrintMeshAssembly meshes)
        {
			// configure settings
			MakerbotSettings settings = new MakerbotSettings();
			//MonopriceSettings settings = new MonopriceSettings(Monoprice.Models.MP_Select_Mini_V2);
			//PrintrbotSettings settings = new PrintrbotSettings(Printrbot.Models.Plus);
            settings.Shells = 2;
            settings.InteriorSolidRegionShells = 0;
            settings.SparseLinearInfillStepX = 5;
            settings.ClipSelfOverlaps = true;
			//settings.LayerRangeFilter = new Interval1i(0, 5);

			//settings.Machine.NozzleDiamMM = 0.75;
			//settings.Machine.MaxLayerHeightMM = 0.5;
			//settings.FillPathSpacingMM = settings.Machine.NozzleDiamMM;
			//settings.LayerHeightMM = 0.5;

			//settings.LayerRangeFilter = new Interval1i(130, 140);

			LastSettings = settings.CloneAs<SingleMaterialFFFSettings>();

            // slice meshes
            MeshPlanarSlicer slicer = new MeshPlanarSlicer() {
                LayerHeightMM = settings.LayerHeightMM
            };
            slicer.Add(meshes);
            PlanarSliceStack slices = slicer.Compute();

            // run print generator
            SingleMaterialFFFPrintGenerator printGen = 
                new SingleMaterialFFFPrintGenerator( meshes, slices, settings );
            printGen.Generate();
            GCodeFile genGCode = printGen.Result;

            string sWritePath = "../../../sample_output/generated.gcode";
            StandardGCodeWriter writer = new StandardGCodeWriter();
            using (StreamWriter w = new StreamWriter(sWritePath)) {
                writer.WriteFile(genGCode, w);
            }
            return sWritePath;
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
				View.QueueDraw();

			} else if (args.Event.Key == Gdk.Key.f) {
				View.ShowFillArea = !View.ShowFillArea;
				View.QueueDraw();

			} else if (args.Event.Key == Gdk.Key.t) {
				View.ShowTravels = !View.ShowTravels;
				View.QueueDraw();

			} else if (args.Event.Key == Gdk.Key.p) {
				View.ShowDepositMoves = !View.ShowDepositMoves;
				View.QueueDraw();

            } else if (args.Event.Key == Gdk.Key.b) {
				View.ShowBelowLayer = !View.ShowBelowLayer;
				View.QueueDraw();

            } else if (args.Event.Key == Gdk.Key.i) {
                View.ShowIssues = !View.ShowIssues;
                View.QueueDraw();

            } else if ( args.Event.Key == Gdk.Key.q ) {
                //SliceViewerTests.TestDGraph2();
                SliceViewerTests.TestFill();
                //SliceViewerTests.TestOffset();
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
				complex.Add(poly);
			}

			PlanarComplex.SolidRegionInfo solids = complex.FindSolidRegions(0.0, false);
			return solids.Polygons[0];
		}



	}
}
