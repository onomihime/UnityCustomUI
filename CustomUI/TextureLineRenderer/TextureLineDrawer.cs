// Copyright (c) 2025 onomihime (github.com/onomihime)
// Originally from: github.com/onomihime/UnityCustomUI
// Licensed under the MIT License. See the LICENSE file in the repository root for full license text.
// This file may be used in commercial projects provided the above copyright notice and this permission notice appear in all copies.

using UnityEngine;
using System.Collections.Generic;
using System; // Needed for Exception
using System.Linq; // Needed for Any


namespace Modules.CustomUI
{
    // Enum to select drawing method
    public enum LineDrawingMethod
    {
        GPU,
        CPU
    }

    [CreateAssetMenu(fileName = "TextureLineDrawer", menuName = "Graphics/Texture Line Drawer")]
    public class TextureLineDrawer : ScriptableObject
    {
        public ComputeShader lineDrawerShader;
        public float defaultLineThickness = 3f;
        public Color defaultLineColor = Color.white;
        public LineDrawingMethod drawingMethod = LineDrawingMethod.GPU; // Selection field

        /// <summary>
        /// Draw a single line on a texture
        /// </summary>
        public Texture2D DrawLine(Texture2D sourceTexture, List<Vector2> points,
            float thickness = -1, Color? color = null)
        {
            List<List<Vector2>> lines = new List<List<Vector2>> { points };
            return DrawLines(sourceTexture, lines, thickness, color);
        }

        /// <summary>
        /// Draw multiple lines on a texture using the selected method, with fallback to CPU on GPU failure.
        /// </summary>
        public Texture2D DrawLines(Texture2D sourceTexture, List<List<Vector2>> lines,
            float thickness = -1, Color? color = null)
        {
            if (sourceTexture == null || lines == null || lines.Count == 0)
                return sourceTexture;

            float lineThickness = thickness > 0 ? thickness : defaultLineThickness;
            Color lineColor = color ?? defaultLineColor;

            Texture2D resultTexture = null;
            bool useGPU = drawingMethod == LineDrawingMethod.GPU;

            // Prerequisite checks for GPU
            if (useGPU)
            {
                if (!SystemInfo.supportsComputeShaders)
                {
                    Debug.LogWarning("Compute shaders not supported on this system. Falling back to CPU line drawing.");
                    useGPU = false;
                }
                else if (lineDrawerShader == null)
                {
                    Debug.LogError("Line Drawer Shader is not assigned. Falling back to CPU line drawing.");
                    useGPU = false;
                }
                // isSupported check is implicitly handled by FindKernel etc. failing later if needed.
                // else if (!lineDrawerShader.isSupported) // Optional: More explicit check
                // {
                //     Debug.LogWarning("Compute shader is not supported by the graphics hardware/driver. Falling back to CPU line drawing.");
                //     useGPU = false;
                // }
            }

            // Attempt GPU drawing if selected and supported
            if (useGPU)
            {
                resultTexture = DrawLinesGPU(sourceTexture, lines, lineThickness, lineColor);
                if (resultTexture == null)
                {
                    Debug.LogWarning("GPU line drawing failed. Falling back to CPU method.");
                    // Fallthrough to CPU method below
                }
            }

            // If GPU wasn't used, failed, or wasn't selected, use CPU
            if (resultTexture == null) // This covers initial selection != GPU OR GPU failure
            {
                 if (drawingMethod == LineDrawingMethod.GPU && useGPU) { /* Already logged warning above */ }
                 else if (drawingMethod == LineDrawingMethod.CPU) { /* Standard CPU path */ }
                 // else { /* Handled by prerequisite checks */ }

                 resultTexture = DrawLinesCPU(sourceTexture, lines, lineThickness, lineColor);
            }

            return resultTexture; // Return either the GPU or CPU result
        }

        /// <summary>
        /// Draw multiple lines on a texture using the GPU Compute Shader. Returns null on failure or invalid input.
        /// </summary>
        private Texture2D DrawLinesGPU(Texture2D sourceTexture, List<List<Vector2>> lines,
            float lineThickness, Color lineColor)
        {
            // --- Input Validation ---
            List<List<Vector2>> validLines = new List<List<Vector2>>();
            for (int lineIdx = 0; lineIdx < lines.Count; lineIdx++)
            {
                var line = lines[lineIdx];

                // Check for null or degenerate lines (fewer than 2 points)
                if (line == null || line.Count < 2)
                {
                    // Silently skip invalid lines
                    continue; 
                }

                // Check coordinate range [0, 1] for all points in the line
                for (int pointIdx = 0; pointIdx < line.Count; pointIdx++)
                {
                    Vector2 p = line[pointIdx];
                    if (p.x < 0f || p.x > 1f || p.y < 0f || p.y > 1f)
                    {
                        Debug.LogWarning($"Invalid coordinate detected in line {lineIdx}, point {pointIdx}: ({p.x}, {p.y}). Coordinates must be in [0, 1] range. Falling back to CPU.");
                        return null; // Trigger CPU fallback
                    }
                }
                
                // Optional: Remove consecutive duplicate points to prevent zero-length segments
                var lineWithoutDuplicates = line.Where((p, index) => index == 0 || !p.Equals(line[index - 1])).ToList();
                if (lineWithoutDuplicates.Count < 2)
                {
                    // Line became degenerate after removing duplicates
                    continue;
                }

                validLines.Add(lineWithoutDuplicates); // Add the validated (and potentially cleaned) line
            }

            // If no valid lines remain after filtering
            if (validLines.Count == 0)
            {
                // Return original texture, nothing valid to draw
                return sourceTexture; 
            }
            // --- End Input Validation ---


            RenderTexture renderTexture = null; 
            ComputeBuffer pointsBuffer = null;
            ComputeBuffer lineInfoBuffer = null;
            RenderTexture prevRT = RenderTexture.active; 
            Texture2D resultTexture = null; 

            try 
            {
                renderTexture = new RenderTexture(sourceTexture.width, sourceTexture.height, 0);
                renderTexture.enableRandomWrite = true;
                renderTexture.Create();

                Graphics.Blit(sourceTexture, renderTexture);

                // Count total points using VALID lines
                int totalPoints = 0;
                foreach (var line in validLines) // Use validLines
                {
                    totalPoints += line.Count;
                }
                // No need to check totalPoints == 0 again, covered by validLines.Count check

                // Create arrays for the buffers based on VALID lines
                Vector4[] pointsArray = new Vector4[totalPoints];
                int[] lineInfoArray = new int[validLines.Count + 1]; // Use validLines.Count

                // Fill the buffers using VALID lines
                int currentIndex = 0;
                for (int i = 0; i < validLines.Count; i++) // Use validLines
                {
                     lineInfoArray[i] = currentIndex;
                     var line = validLines[i]; // Use validLines

                    for (int j = 0; j < line.Count; j++)
                    {
                        pointsArray[currentIndex] = new Vector4(line[j].x, line[j].y, 0, 0);
                        currentIndex++;
                    }
                }
                lineInfoArray[validLines.Count] = totalPoints; // Use validLines.Count


                pointsBuffer = new ComputeBuffer(pointsArray.Length > 0 ? pointsArray.Length : 1, sizeof(float) * 4); 
                if (pointsArray.Length > 0) pointsBuffer.SetData(pointsArray);

                lineInfoBuffer = new ComputeBuffer(lineInfoArray.Length > 0 ? lineInfoArray.Length : 1, sizeof(int)); 
                if (lineInfoArray.Length > 0) lineInfoBuffer.SetData(lineInfoArray);


                // --- Start Inner Try-Catch for GPU Execution ---
                try 
                {
                    int kernelIndex = lineDrawerShader.FindKernel("CSMain");
                    if (kernelIndex < 0) throw new Exception("Failed to find kernel 'CSMain' in compute shader.");

                    lineDrawerShader.SetTexture(kernelIndex, "Result", renderTexture);
                    lineDrawerShader.SetBuffer(kernelIndex, "Points", pointsBuffer);
                    lineDrawerShader.SetBuffer(kernelIndex, "LineInfo", lineInfoBuffer);
                    lineDrawerShader.SetInt("LineCount", validLines.Count); // Use validLines.Count
                    lineDrawerShader.SetInt("Width", sourceTexture.width);
                    lineDrawerShader.SetInt("Height", sourceTexture.height);
                    lineDrawerShader.SetFloat("Thickness", lineThickness);
                    lineDrawerShader.SetVector("LineColor", lineColor);

                    lineDrawerShader.Dispatch(kernelIndex, Mathf.CeilToInt(sourceTexture.width / 8f),
                        Mathf.CeilToInt(sourceTexture.height / 8f), 1);

                    // Copy result back to a new texture
                    resultTexture = new Texture2D(sourceTexture.width, sourceTexture.height,
                        sourceTexture.format, false);

                    RenderTexture.active = renderTexture;
                    resultTexture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
                    resultTexture.Apply();
                    // Restore happens in finally
                }
                catch (Exception gpuEx)
                {
                    Debug.LogWarning($"Error during GPU line drawing execution: {gpuEx.Message}\n{gpuEx.StackTrace}");
                    // Ensure resultTexture is null to indicate failure
                    if (resultTexture != null) DestroyImmediate(resultTexture); // Clean up partially created texture if ReadPixels failed
                    resultTexture = null; 
                    // Let finally block handle resource cleanup
                }
                // --- End Inner Try-Catch ---
            }
            catch (Exception setupEx) // Catch exceptions during setup (RT creation, buffer creation etc.)
            {
                 Debug.LogWarning($"Error during GPU line drawing setup: {setupEx.Message}\n{setupEx.StackTrace}");
                 if (resultTexture != null) DestroyImmediate(resultTexture);
                 resultTexture = null;
                 // Let finally block handle resource cleanup
            }
            finally
            {
                // --- Diagnostics Removed for Brevity ---

                // Clean up compute buffers first
                if (pointsBuffer != null) pointsBuffer.Release();
                if (lineInfoBuffer != null) lineInfoBuffer.Release();

                // Handle render texture state and release
                if (renderTexture != null)
                {
                    if (RenderTexture.active == renderTexture)
                    {
                        if (prevRT != renderTexture)
                        {
                            RenderTexture.active = prevRT;
                        }
                        else
                        {
                            RenderTexture.active = null;
                        }
                    }
                    renderTexture.Release();
                }
                // If renderTexture creation failed, but prevRT was stored
                else if (RenderTexture.active != prevRT)
                {
                     RenderTexture.active = prevRT;
                }
            }

            // If resultTexture is still null here, it means an exception occurred or input was invalid.
            // If it's not null, the GPU path succeeded.
            return resultTexture;
        }


        /// <summary>
        /// Draw multiple lines on a texture using the CPU
        /// </summary>
        private Texture2D DrawLinesCPU(Texture2D sourceTexture, List<List<Vector2>> lines,
            float thickness, Color color)
        {
            Texture2D resultTexture = new Texture2D(sourceTexture.width, sourceTexture.height, sourceTexture.format, false);
            Graphics.CopyTexture(sourceTexture, resultTexture); // Efficient copy

            Color[] pixels = resultTexture.GetPixels(); // Work on pixel array for performance
            int width = resultTexture.width;
            int height = resultTexture.height;
            float halfThickness = thickness / 2.0f;
            float sqrRadius = halfThickness * halfThickness; // Use squared distance for efficiency

            foreach (var line in lines)
            {
                if (line == null || line.Count < 2) continue;

                for (int i = 0; i < line.Count - 1; i++)
                {
                    // Convert normalized coordinates to pixel coordinates
                    Vector2 p1 = new Vector2(line[i].x * width, line[i].y * height);
                    Vector2 p2 = new Vector2(line[i+1].x * width, line[i+1].y * height);

                    // Calculate bounding box for the thickened segment
                    float minX = Mathf.Min(p1.x, p2.x) - halfThickness;
                    float maxX = Mathf.Max(p1.x, p2.x) + halfThickness;
                    float minY = Mathf.Min(p1.y, p2.y) - halfThickness;
                    float maxY = Mathf.Max(p1.y, p2.y) + halfThickness;

                    // Clamp bounding box to texture dimensions and convert to integer pixel indices
                    int startX = Mathf.Max(0, Mathf.FloorToInt(minX));
                    int endX = Mathf.Min(width, Mathf.CeilToInt(maxX));
                    int startY = Mathf.Max(0, Mathf.FloorToInt(minY));
                    int endY = Mathf.Min(height, Mathf.CeilToInt(maxY));

                    // Iterate within bounding box
                    for (int y = startY; y < endY; y++)
                    {
                        for (int x = startX; x < endX; x++)
                        {
                            Vector2 pixelCenter = new Vector2(x + 0.5f, y + 0.5f); // Use pixel center for distance check
                            if (DistancePointToLineSegmentSquared(pixelCenter, p1, p2) <= sqrRadius)
                            {
                                pixels[y * width + x] = color;
                            }
                        }
                    }
                }
                 // Optional: Draw circles at each point for smoother joins/caps if needed
                 // foreach(var point in line) { DrawCircle(pixels, width, height, new Vector2(point.x * width, point.y * height), halfThickness, color); }
            }

            resultTexture.SetPixels(pixels);
            resultTexture.Apply();
            return resultTexture;
        }

        /// <summary>
        /// Calculates the squared distance from a point to a line segment.
        /// </summary>
        private float DistancePointToLineSegmentSquared(Vector2 point, Vector2 p1, Vector2 p2)
        {
            float l2 = (p1 - p2).sqrMagnitude;
            if (l2 == 0.0f) return (point - p1).sqrMagnitude; // Segment is a point
            // Project point onto the line containing the segment, clamping t between 0 and 1.
            float t = Mathf.Clamp01(Vector2.Dot(point - p1, p2 - p1) / l2);
            // Calculate the closest point on the segment
            Vector2 projection = p1 + t * (p2 - p1);
            // Return squared distance from point to projection
            return (point - projection).sqrMagnitude;
        }

        // Optional: Helper to draw filled circles if needed for caps or points
        // private void DrawCircle(Color[] pixels, int width, int height, Vector2 center, float radius, Color color) { ... implementation ... }

    }
}