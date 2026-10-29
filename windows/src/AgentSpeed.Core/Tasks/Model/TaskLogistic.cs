namespace AgentSpeed.Core.Tasks;

/// <summary>
/// The MAP fit of a logistic model with a Gaussian prior centred on the prior coefficients (METRICS 6.11
/// <i>Calibration</i> step 4): Newton–Raphson (IRLS) from the prior, exactly 25 iterations, each solving
/// <c>H·δ = g</c> by Cholesky. Sums run in the reference's order.
/// </summary>
internal static class TaskLogistic
{
    private const double L2 = 4.0;
    private const int Iterations = 25;

    public static double[] Fit(TaskModelRows rows, double[] prior)
    {
        var beta = (double[])prior.Clone();
        var d = beta.Length;
        var gradient = new double[d];
        var hessian = new double[d, d];
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            GradientAndHessian(beta, prior, rows, gradient, hessian);
            var step = CholeskySolve(hessian, gradient);
            for (var k = 0; k < d; k++)
            {
                beta[k] -= step[k];
            }
        }

        return beta;
    }

    public static double[] Gradient(IReadOnlyList<double> beta, double[] prior, TaskModelRows rows)
    {
        var gradient = new double[beta.Count];
        GradientAndHessian(beta, prior, rows, gradient, new double[beta.Count, beta.Count]);
        return gradient;
    }

    /// <summary><c>g = Σ s·(p − y)·x + λ·(β − β⁰)</c> and <c>H = Σ s·p·(1 − p)·x·xᵀ + λ·I</c>.</summary>
    private static void GradientAndHessian(IReadOnlyList<double> beta, double[] prior, TaskModelRows rows, double[] gradient, double[,] hessian)
    {
        var d = beta.Count;
        if (d != rows.Width || prior.Length != d)
        {
            throw new ArgumentException("the coefficients do not match the model's features", nameof(beta));
        }

        for (var i = 0; i < d; i++)
        {
            gradient[i] = L2 * (beta[i] - prior[i]);
            for (var j = 0; j < d; j++)
            {
                hessian[i, j] = i == j ? L2 : 0.0;
            }
        }

        for (var row = 0; row < rows.Count; row++)
        {
            var eta = 0.0;
            for (var k = 0; k < d; k++)
            {
                eta += beta[k] * rows.X(row, k);
            }

            var p = TaskPredictions.Sigmoid(eta);
            var weight = rows.Weight(row);
            var residual = weight * (p - rows.Label(row));
            var curvature = weight * p * (1 - p);
            for (var i = 0; i < d; i++)
            {
                var xi = rows.X(row, i);
                gradient[i] += residual * xi;
                var scaled = curvature * xi;
                for (var j = 0; j < d; j++)
                {
                    hessian[i, j] += scaled * rows.X(row, j);
                }
            }
        }
    }

    /// <summary><c>cholesky_solve</c>: <c>H = L·Lᵀ</c> column by column, then forward and back substitution.</summary>
    private static double[] CholeskySolve(double[,] matrix, double[] vector)
    {
        var d = vector.Length;
        var lower = new double[d, d];
        for (var j = 0; j < d; j++)
        {
            var diagonal = 0.0;
            for (var k = 0; k < j; k++)
            {
                diagonal += lower[j, k] * lower[j, k];
            }

            lower[j, j] = Math.Sqrt(matrix[j, j] - diagonal);
            for (var i = j + 1; i < d; i++)
            {
                var sum = 0.0;
                for (var k = 0; k < j; k++)
                {
                    sum += lower[i, k] * lower[j, k];
                }

                lower[i, j] = (matrix[i, j] - sum) / lower[j, j];
            }
        }

        var z = new double[d];
        for (var i = 0; i < d; i++)
        {
            var sum = 0.0;
            for (var k = 0; k < i; k++)
            {
                sum += lower[i, k] * z[k];
            }

            z[i] = (vector[i] - sum) / lower[i, i];
        }

        var x = new double[d];
        for (var i = d - 1; i >= 0; i--)
        {
            var sum = 0.0;
            for (var k = i + 1; k < d; k++)
            {
                sum += lower[k, i] * x[k];
            }

            x[i] = (z[i] - sum) / lower[i, i];
        }

        return x;
    }
}
