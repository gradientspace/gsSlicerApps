using System;
using System.IO;
using System.Collections.Generic;
using g3;
using gs;
using gs.info;

namespace GeneratedPathsDemo
{
    class Program
    {
        static void Main(string[] args)
        {
            var file_accumulator = new GCodeFileAccumulator();
            var builder = new GCodeBuilder(file_accumulator);
            MakerbotSettings settings = new MakerbotSettings();

            SingleMaterialFFFCompiler compiler = new SingleMaterialFFFCompiler(
                builder, settings, MakerbotAssembler.Factory);
            settings.ExtruderTempC = 200;

            compiler.Begin();

            //generate_stacked_polygon(compiler, settings);
            //generate_stacked_wavy_circle(compiler, settings);
            //generate_square(compiler, settings);
            //generate_vertical(compiler, settings);
            generate_vertical_wave(compiler, settings);

            compiler.End();

            GCodeFile gcode = file_accumulator.File;
            using (StreamWriter w = new StreamWriter("c:\\demo\\generated.gcode")) {
                StandardGCodeWriter writer = new StandardGCodeWriter();
                writer.WriteFile(gcode, w);
            }
        }



        static void generate_stacked_polygon(SingleMaterialFFFCompiler compiler,
                                             SingleMaterialFFFSettings settings)
        {
            for (int layer_i = 0; layer_i < 10; ++layer_i) {

                // create data structures for organizing this layer
                ToolpathSetBuilder layer_builder = new ToolpathSetBuilder();
                SequentialScheduler2d scheduler = new SequentialScheduler2d(layer_builder, settings);
                if (layer_i == 0)
                    scheduler.SpeedHint = SchedulerSpeedHint.Careful;

                // initialize layer
                layer_builder.Initialize(compiler.NozzlePosition);

                // layer-up
                layer_builder.AppendZChange(settings.LayerHeightMM, settings.ZTravelSpeed);

                // schedule a circle
                FillPolygon2d circle_poly = new FillPolygon2d(Polygon2d.MakeCircle(25.0f, 64));
                circle_poly.TypeFlags = FillTypeFlags.OuterPerimeter;
                scheduler.AppendPolygon2d(circle_poly);

                // pass paths to compiler
                compiler.AppendPaths(layer_builder.Paths);
            }
        }





        static void generate_stacked_wavy_circle(SingleMaterialFFFCompiler compiler,
                                                 SingleMaterialFFFSettings settings)
        {
            double height = 20.0;  // mm
            int NLayers = (int)(height / settings.LayerHeightMM);  // 20mm
            int NSteps = 128;
            double radius = 15.0;
            double frequency = 6;
            double scale = 5.0;

            for (int layer_i = 0; layer_i < NLayers; ++layer_i) {

                // create data structures for organizing this layer
                ToolpathSetBuilder layer_builder = new ToolpathSetBuilder();
                SequentialScheduler2d scheduler = new SequentialScheduler2d(layer_builder, settings);
                if (layer_i == 0)
                    scheduler.SpeedHint = SchedulerSpeedHint.Careful;

                // initialize and layer-up
                layer_builder.Initialize(compiler.NozzlePosition);
                layer_builder.AppendZChange(settings.LayerHeightMM, settings.ZTravelSpeed);

                // start with circle
                FillPolygon2d circle_poly = new FillPolygon2d(Polygon2d.MakeCircle(radius, NSteps));
                circle_poly.TypeFlags = FillTypeFlags.OuterPerimeter;

                // apply a wave deformation to circle, with wave height increasing with Z
                double layer_scale = MathUtil.Lerp(0, scale, (double)layer_i / (double)NLayers);
                for ( int i = 0; i < NSteps; ++i ) {
                    Vector2d v = circle_poly[i];
                    double angle = Math.Atan2(v.y, v.x);
                    double r = v.Length;
                    r += layer_scale * Math.Sin(frequency * angle);
                    circle_poly[i] = r * v.Normalized;
                }

                scheduler.AppendPolygon2d(circle_poly);

                // pass paths to compiler
                compiler.AppendPaths(layer_builder.Paths);
            }
        }




        static void generate_square(SingleMaterialFFFCompiler compiler,
                                          SingleMaterialFFFSettings settings)
        {

            ToolpathSetBuilder builder = new ToolpathSetBuilder();
            builder.Initialize(compiler.NozzlePosition);

            // layer-up
            builder.AppendZChange(settings.LayerHeightMM, settings.ZTravelSpeed);

            // draw rectangle
            float left = 0, right = 50, bottom = -20, top = 20;
            builder.AppendTravel(new Vector2d(left, bottom), settings.RapidTravelSpeed);
            builder.AppendExtrude(new Vector2d(right, bottom), settings.CarefulExtrudeSpeed);
            builder.AppendExtrude(new Vector2d(right, top), settings.CarefulExtrudeSpeed);
            builder.AppendExtrude(new Vector2d(left, top), settings.CarefulExtrudeSpeed);
            builder.AppendExtrude(new Vector2d(left, bottom), settings.CarefulExtrudeSpeed);

            compiler.AppendPaths(builder.Paths);
        }



        static void generate_vertical(SingleMaterialFFFCompiler compiler,
                                      SingleMaterialFFFSettings settings)
        {

            ToolpathSetBuilder builder = new ToolpathSetBuilder();
            builder.Initialize(compiler.NozzlePosition);

            // layer-up
            builder.AppendZChange(settings.LayerHeightMM, settings.ZTravelSpeed);

            // draw circle
            int N = 32;
            Polygon2d circle = Polygon2d.MakeCircle(25.0f, N, -MathUtil.HalfPI);

            builder.AppendTravel(circle[0], settings.RapidTravelSpeed);
            for (int k = 1; k <= N; k++)
                builder.AppendExtrude(circle[k%N], settings.CarefulExtrudeSpeed);

            // layer-up
            builder.AppendZChange(settings.LayerHeightMM, settings.ZTravelSpeed);

            double height = 5.0f;
            double z_layer = builder.Position.z;
            double z_high = z_layer + height;
            for ( int k = 1; k <= N; k++ ) {
                Vector2d p2 = circle[k%N];
                double z = (k % 2 == 1) ? z_high : z_layer;
                Vector3d p3 = new Vector3d(p2.x, p2.y, z);
                builder.AppendExtrude(p3, settings.CarefulExtrudeSpeed / 4);

                // dwell at tops
                if ( z == z_high ) {
                    AssemblerCommandsToolpath dwell_path = new AssemblerCommandsToolpath() {
                        AssemblerF = make_tip
                    };
                    builder.AppendPath(dwell_path);
                }
            }

            // move up to z_high
            builder.AppendZChange(height, settings.ZTravelSpeed);

            // draw circle again
            builder.AppendTravel(circle[0], settings.RapidTravelSpeed);
            for (int k = 1; k <= N; k++)
                builder.AppendExtrude(circle[k % N], settings.RapidExtrudeSpeed);


            // draw teeth again

            z_layer = builder.Position.z;
            z_high = z_layer + height;
            for (int k = 1; k <= N; k++) {
                Vector2d p2 = circle[k % N];
                double z = (k % 2 == 1) ? z_high : z_layer;
                Vector3d p3 = new Vector3d(p2.x, p2.y, z);
                builder.AppendExtrude(p3, settings.CarefulExtrudeSpeed / 4);

                // dwell at tops
                if (z == z_high) {
                    AssemblerCommandsToolpath dwell_path = new AssemblerCommandsToolpath() {
                        AssemblerF = make_tip
                    };
                    builder.AppendPath(dwell_path);
                }
            }


            // move up to z_high
            builder.AppendZChange(height, settings.ZTravelSpeed);

            // draw circle again
            builder.AppendTravel(circle[0], settings.RapidTravelSpeed);
            for (int k = 1; k <= N; k++)
                builder.AppendExtrude(circle[k % N], settings.RapidExtrudeSpeed);


            compiler.AppendPaths(builder.Paths);
        }









        static void generate_vertical_wave(SingleMaterialFFFCompiler compiler,
                                           SingleMaterialFFFSettings settings)
        {

            ToolpathSetBuilder builder = new ToolpathSetBuilder();
            builder.Initialize(compiler.NozzlePosition);

            // layer-up
            builder.AppendZChange(settings.LayerHeightMM, settings.ZTravelSpeed);

            // draw circle
            int N = 256;
            Polygon2d circle = Polygon2d.MakeCircle(15.0f, N, -MathUtil.HalfPI);

            builder.AppendTravel(circle[0], settings.RapidTravelSpeed);
            for (int k = 1; k <= N; k++)
                builder.AppendExtrude(circle[k % N], settings.CarefulExtrudeSpeed);

            // layer-up
            builder.AppendZChange(settings.LayerHeightMM, settings.ZTravelSpeed);

            double freq = 12;
            double height = 3.0f;
            double z_layer = builder.Position.z;
            for (int k = 1; k <= N; k++) {
                Vector2d p2 = circle[k % N];
                double angle = Math.Atan2(p2.y, p2.x);

                double t = Math.Max(0, Math.Sin(freq * angle));
                double z = z_layer + t * height;
                Vector3d p3 = new Vector3d(p2.x, p2.y, z);
                builder.AppendExtrude(p3, settings.CarefulExtrudeSpeed / 8);
            }

            // move up to z_high
            builder.AppendZChange(height, settings.ZTravelSpeed);

            // draw circle again
            builder.AppendTravel(circle[0], settings.RapidTravelSpeed);
            for (int k = 1; k <= N; k++)
                builder.AppendExtrude(circle[k % N], settings.RapidExtrudeSpeed);

            compiler.AppendPaths(builder.Paths);
        }








        static void make_tip(IDepositionAssembler iassember, ThreeAxisPrinterCompiler icompiler)
        {
            BaseDepositionAssembler assembler = iassember as BaseDepositionAssembler;
            assembler.FlushQueues();
            assembler.BeginRetractRelativeDist(assembler.NozzlePosition, 9999, -1.0f);
            assembler.AppendDwell(500);
            assembler.EndRetract(assembler.NozzlePosition, 9999);
        }



    }
}
