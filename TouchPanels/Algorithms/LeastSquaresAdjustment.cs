using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;
using Windows.ApplicationModel.Resources;

namespace TouchPanels.Algorithms
{
	/// <summary>
	/// Performs a least squares adjustment between input and output points and
	/// returns the conversion parameters.
	/// </summary>
	internal class LeastSquaresAdjustment
	{
		private readonly List<Point> _inputs;
		private readonly List<Point> _outputs;

		/// <summary>
		/// Initialize Least Squares transformations
		/// </summary>
		public LeastSquaresAdjustment() : this(Enumerable.Empty<Point>(), Enumerable.Empty<Point>())
		{
		}

		/// <summary>
		/// Initialize Least Squares transformations
		/// </summary>
		public LeastSquaresAdjustment(IEnumerable<Point> inputs, IEnumerable<Point> outputs)
		{
			_inputs = new List<Point>(inputs);
			_outputs = new List<Point>(outputs);
			if (_inputs.Count != _outputs.Count)
				throw new ArgumentException(ResourceLoader.GetForCurrentView().GetString("PointsException"));
		}

		/// <summary>
		/// Adds an input and output value pair to the collection
		/// </summary>
		/// <param name="input"></param>
		/// <param name="output"></param>
		public void AddInputOutputPoint(Point input, Point output)
		{
			_inputs.Add(input);
			_outputs.Add(output);
		}

		/// <summary>
		/// Removes input and output value pair at the specified index
		/// </summary>
		/// <param name="i"></param>
		public void RemoveInputOutputPointAt(int i)
		{
			_inputs.RemoveAt(i);
			_outputs.RemoveAt(i);
		}

		/// <summary>
		/// Gets the input point value at the specified index
		/// </summary>
		/// <param name="i">index</param>
		/// <returns>Input point value a index 'i'</returns>
		public Point GetInputPoint(int i)
		{
			return _inputs[i];
		}

		/// <summary>
		/// Sets the input point value at the specified index
		/// </summary>
		/// <param name="p">Point value</param>
		/// <param name="i">index</param>
		public void SetInputPointAt(Point p, int i)
		{
			_inputs[i] = p;
		}

		/// <summary>
		/// Gets the output point value at the specified index
		/// </summary>
		/// <param name="i">index</param>
		/// <returns>Output point value a index 'i'</returns>
		public Point GetOutputPoint(int i)
		{
			return _outputs[i];
		}

		/// <summary>
		/// Sets the output point value at the specified index
		/// </summary>
		/// <param name="p">Point value</param>
		/// <param name="i">index</param>
		public void SetOutputPointAt(Point p, int i)
		{
			_outputs[i] = p;
		}

		/// <summary>
		/// Returns transformation parameters to coordinatesystem
		/// Return an array with the six affine transformation parameters {a,b,c,d,e,f}
		/// a,b defines vector 1 of coordinate system, d,e vector 2.
		/// c,f defines image center.
		/// Converting from input (X,Y) to output coordinate system (X',Y') is done by:
		/// X' = a*X + b*Y + c, Y' = d*X + e*Y + f
		/// Transformation based on Mikhail "Introduction to Modern Photogrammetry" p. 399-300
		/// Extended to arbitrary number of points by M. Nielsen
		/// </summary>
		/// <returns>Six transformation parameters a,b,c,d,e,f for the affine transformation</returns>
		public AffineTransformationParameters GetTransformation()
		{
			if (_inputs.Count < 3)
				throw new System.Exception(ResourceLoader.GetForCurrentView().GetString("MeasurementsException"));

			int count = _inputs.Count;
            Point[] outputs = _outputs.ToArray();
			Point[] inputs = _inputs.ToArray();
			double[][] N = CreateMatrix(3, 3);
			//Create normal equation: transpose(B)*B
			//B: matrix of calibrated values. Example of row in B: [x , y , -1]
			for (int i = 0; i < count; i++)
			{
				N[0][0] += Math.Pow(outputs[i].X, 2);
				N[0][1] += outputs[i].X * outputs[i].Y;
				N[0][2] += -outputs[i].X;
				N[1][1] += Math.Pow(outputs[i].Y, 2);
				N[1][2] += -outputs[i].Y;
			}
			N[2][2] = count;

			double[] t1 = new double[3];
			double[] t2 = new double[3];

			for (int i = 0; i < count; i++)
			{
				t1[0] += outputs[i].X * inputs[i].X;
				t1[1] += outputs[i].Y * inputs[i].X;
				t1[2] += -inputs[i].X;

				t2[0] += outputs[i].X * inputs[i].Y;
				t2[1] += outputs[i].Y * inputs[i].Y;
				t2[2] += -inputs[i].Y;
			}
			
			// Solve equation N = transpose(B)*t1
			var result = new AffineTransformationParameters();
            double frac = 1 / (-N[0][0] * N[1][1] * N[2][2] + N[0][0] * Math.Pow(N[1][2], 2) + Math.Pow(N[0][1], 2) * N[2][2] - 2 * N[1][2] * N[0][1] * N[0][2] + N[1][1] * Math.Pow(N[0][2], 2));
			result.A = (-N[0][1] * N[1][2] * t1[2] + N[0][1] * t1[1] * N[2][2] - N[0][2] * N[1][2] * t1[1] + N[0][2] * N[1][1] * t1[2] - t1[0] * N[1][1] * N[2][2] + t1[0] * Math.Pow(N[1][2], 2)) * frac;
			result.B = (-N[0][1] * N[0][2] * t1[2] + N[0][1] * t1[0] * N[2][2] + N[0][0] * N[1][2] * t1[2] - N[0][0] * t1[1] * N[2][2] - N[0][2] * N[1][2] * t1[0] + Math.Pow(N[0][2], 2) * t1[1]) * frac;
			result.C = -(-N[1][2] * N[0][1] * t1[0] + Math.Pow(N[0][1], 2) * t1[2] + N[0][0] * N[1][2] * t1[1] - N[0][0] * N[1][1] * t1[2] - N[0][2] * N[0][1] * t1[1] + N[1][1] * N[0][2] * t1[0]) * frac;
			// Solve equation N = transpose(B)*t2
			result.D = (-N[0][1] * N[1][2] * t2[2] + N[0][1] * t2[1] * N[2][2] - N[0][2] * N[1][2] * t2[1] + N[0][2] * N[1][1] * t2[2] - t2[0] * N[1][1] * N[2][2] + t2[0] * Math.Pow(N[1][2], 2)) * frac;
			result.E = (-N[0][1] * N[0][2] * t2[2] + N[0][1] * t2[0] * N[2][2] + N[0][0] * N[1][2] * t2[2] - N[0][0] * t2[1] * N[2][2] - N[0][2] * N[1][2] * t2[0] + Math.Pow(N[0][2], 2) * t2[1]) * frac;
			result.F = -(-N[1][2] * N[0][1] * t2[0] + Math.Pow(N[0][1], 2) * t2[2] + N[0][0] * N[1][2] * t2[1] - N[0][0] * N[1][1] * t2[2] - N[0][2] * N[0][1] * t2[1] + N[1][1] * N[0][2] * t2[0]) * frac;
			
			//Calculate s0
			double s0 = 0;
			for (int i = 0; i < this._inputs.Count; i++)
			{
				var tt = result.Transform(_outputs[i]);
				s0 += Math.Pow(tt.X - _inputs[i].X, 2) + Math.Pow(tt.Y - _inputs[i].Y, 2);
			}
			result.S0 = Math.Sqrt(s0) / (this._inputs.Count);
			return result;
		}

		/// <summary>
		/// Creates an n x m matrix of doubles
		/// </summary>
		/// <param name="n">width of matrix</param>
		/// <param name="m">height of matrix</param>
		/// <returns>n*m matrix</returns>
		private static double[][] CreateMatrix(int n, int m) 
		{
			double[][] N = new double[n][];
			for(int i=0;i<n;i++) 
			{
				N[i] = new double[m];
			}
			return N;
		}
	}

    public class AffineTransformationParameters
	{
		public double A { get; set; }
		public double B { get; set; }
		public double C { get; set; }
		public double D { get; set; }
		public double E { get; set; }
		public double F { get; set; }
		public double S0 { get; set; }
		public Point Transform(Point input)
		{
			return new Point(
			 A * input.X + B * input.Y + C,
			 D * input.X + E * input.Y + F
			 );
		}
	}
}
