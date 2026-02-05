using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class PixelHeightmap : MonoBehaviour
{
    [Header("Setup")]
    public Texture2D[] frames; // Array of 24 images
    public float pixelSpacing = 1f;
    public float maxHeight = 10f;
    public float minHeight = 0f;
    
    [Header("Performance")]
    [Tooltip("Downsample images to this resolution (0 = use full resolution)")]
    public int maxResolution = 130; // Set to 0 for full 377x377
    
    [Tooltip("Enable physics collision (VERY EXPENSIVE - causes lag)")]
    public bool enableCollision = false;
    
    [Tooltip("Update collision every N frames (higher = better performance)")]
    [Range(1, 10)]
    public int collisionUpdateInterval = 3;
    
    [Tooltip("Recalculate normals for lighting (expensive but looks better)")]
    public bool recalculateNormals = true;
    
    [Tooltip("Update normals every N frames (higher = better performance)")]
    [Range(1, 10)]
    public int normalUpdateInterval = 2;
    
    [Header("Animation")]
    [Tooltip("Time to smoothly transition between frames")]
    public float transitionDuration = 1f;
    
    [Tooltip("Time to wait on each frame before transitioning to next")]
    public float frameHoldDuration = 0.5f;
    
    [Tooltip("Auto-play animation on start")]
    public bool autoPlay = true;
    
    [Tooltip("Loop the animation")]
    public bool loop = true;
    
    [Header("Visuals")]
    [Tooltip("Apply pixel colors from texture to cubes")]
    public bool usePixelColors = true;
    
    [Tooltip("Brightness multiplier for colors (lower = darker)")]
    [Range(0f, 2f)]
    public float colorBrightness = 1f;
    
    [Tooltip("Tint to blend with all colors")]
    public Color tintColor = Color.white;
    
    [Header("Contrast & Depth")]
    [Tooltip("Darken lower cubes based on height (creates depth perception)")]
    [Range(0f, 1f)]
    public float heightDarkening = 0.5f;
    
    [Tooltip("Minimum brightness for darkened cubes (prevents pure black)")]
    [Range(0f, 1f)]
    public float minDarkenedBrightness = 0.2f;
    
    [Tooltip("Increase color saturation for more vivid colors")]
    [Range(0f, 2f)]
    public float saturation = 1f;
    
    [Tooltip("Apply gradient from bottom to top of each cube")]
    public bool useVerticalGradient = true;
    
    [Tooltip("How much darker the bottom of cubes should be")]
    [Range(0f, 1f)]
    public float gradientStrength = 0.3f;
    
    private Mesh mesh;
    private MeshCollider meshCollider;
    private Vector3[] vertices;
    private Color[] vertexColors;
    private float[] currentHeights;
    private float[] targetHeights;
    private Color[] currentColors;
    private Color[] targetColors;
    private int width;
    private int height;
    private int updateCounter = 0;
    
    // Animation state
    private int currentFrameIndex = 0;
    private float transitionProgress = 0f;
    private float holdTimer = 0f;
    private bool isTransitioning = false;
    private bool isPlaying = false;
    
    // Cube geometry (24 vertices per cube, 6 faces × 4 verts)
    private static readonly Vector3[] cubeVertOffsets = new Vector3[24]
    {
        // Bottom face (y=0)
        new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f),
        // Top face (y=1)
        new Vector3(-0.5f, 1, -0.5f), new Vector3(0.5f, 1, -0.5f), new Vector3(0.5f, 1, 0.5f), new Vector3(-0.5f, 1, 0.5f),
        // Front face (z=0.5)
        new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(0.5f, 1, 0.5f), new Vector3(-0.5f, 1, 0.5f),
        // Back face (z=-0.5)
        new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 1, -0.5f), new Vector3(-0.5f, 1, -0.5f),
        // Left face (x=-0.5)
        new Vector3(-0.5f, 0, -0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(-0.5f, 1, 0.5f), new Vector3(-0.5f, 1, -0.5f),
        // Right face (x=0.5)
        new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(0.5f, 1, 0.5f), new Vector3(0.5f, 1, -0.5f)
    };
    
    private static readonly int[] cubeTriangles = new int[36]
    {
        0,1,2, 0,2,3,       // Bottom
        4,7,6, 4,6,5,       // Top
        8,9,10, 8,10,11,    // Front
        12,15,14, 12,14,13, // Back
        16,17,18, 16,18,19, // Left
        20,23,22, 20,22,21  // Right
    };

    void Start()
    {
        if (frames == null || frames.Length == 0)
        {
            Debug.LogError("No frames assigned!");
            return;
        }
        
        InitializeHeightmap();
        
        if (autoPlay)
        {
            Play();
        }
    }

    void InitializeHeightmap()
    {
        // Determine actual resolution to use
        int originalWidth = frames[0].width;
        int originalHeight = frames[0].height;
        
        if (maxResolution > 0 && (originalWidth > maxResolution || originalHeight > maxResolution))
        {
            // Downsample to maxResolution
            float scale = Mathf.Min((float)maxResolution / originalWidth, (float)maxResolution / originalHeight);
            width = Mathf.RoundToInt(originalWidth * scale);
            height = Mathf.RoundToInt(originalHeight * scale);
            Debug.Log($"Downsampling from {originalWidth}x{originalHeight} to {width}x{height}");
        }
        else
        {
            width = originalWidth;
            height = originalHeight;
            Debug.Log($"Using full resolution: {width}x{height}");
        }
        
        int cubeCount = width * height;
        currentHeights = new float[cubeCount];
        targetHeights = new float[cubeCount];
        currentColors = new Color[cubeCount];
        targetColors = new Color[cubeCount];
        
        // Load first frame
        LoadFrame(0, true);
        
        GenerateMesh();
        
        // Setup collision if enabled
        if (enableCollision)
        {
            if (meshCollider == null)
            {
                meshCollider = gameObject.AddComponent<MeshCollider>();
            }
            meshCollider.sharedMesh = mesh;
            Debug.Log("Collision enabled - this will impact performance!");
        }
        else
        {
            if (meshCollider != null)
            {
                Destroy(meshCollider);
                meshCollider = null;
            }
            Debug.Log("Collision disabled for better performance");
        }
    }

    void LoadFrame(int frameIndex, bool immediate = false)
    {
        if (frameIndex < 0 || frameIndex >= frames.Length)
        {
            Debug.LogError($"Invalid frame index: {frameIndex}");
            return;
        }
        
        Color[] pixels = GetResampledPixels(frames[frameIndex]);
        
        // Calculate target heights and colors from this frame
        for (int i = 0; i < pixels.Length; i++)
        {
            float brightness = pixels[i].grayscale;
            targetHeights[i] = Mathf.Lerp(minHeight, maxHeight, brightness);
            targetColors[i] = CalculateCubeColor(pixels[i], targetHeights[i]);
            
            if (immediate)
            {
                currentHeights[i] = targetHeights[i];
                currentColors[i] = targetColors[i];
            }
        }
        
        currentFrameIndex = frameIndex;
    }

    Color[] GetResampledPixels(Texture2D texture)
    {
        if (texture.width == width && texture.height == height)
        {
            // No resampling needed
            return texture.GetPixels();
        }
        
        // Downsample texture
        RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        RenderTexture.active = rt;
        
        Graphics.Blit(texture, rt);
        
        Texture2D tempTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tempTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tempTexture.Apply();
        
        Color[] pixels = tempTexture.GetPixels();
        
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);
        Destroy(tempTexture);
        
        return pixels;
    }

    void GenerateMesh()
    {
        mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        
        int cubeCount = width * height;
        int vertexCount = cubeCount * 24;
        int triangleCount = cubeCount * 36;
        
        vertices = new Vector3[vertexCount];
        vertexColors = new Color[vertexCount];
        int[] triangles = new int[triangleCount];
        
        // Generate geometry
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int pixelIndex = y * width + x;
                int vertexStartIndex = pixelIndex * 24;
                int triangleStartIndex = pixelIndex * 36;
                
                Vector3 basePosition = new Vector3(x * pixelSpacing, 0, y * pixelSpacing);
                float cubeHeight = currentHeights[pixelIndex];
                Color baseCubeColor = currentColors[pixelIndex];
                
                // Create cube vertices with per-vertex coloring
                for (int v = 0; v < 24; v++)
                {
                    Vector3 offset = cubeVertOffsets[v];
                    offset.x *= pixelSpacing;
                    offset.z *= pixelSpacing;
                    offset.y *= cubeHeight;
                    
                    vertices[vertexStartIndex + v] = basePosition + offset;
                    
                    // Apply vertical gradient if enabled
                    Color vertexColor = baseCubeColor;
                    if (useVerticalGradient && cubeHeight > 0.01f)
                    {
                        float normalizedHeight = offset.y / cubeHeight;
                        float darken = Mathf.Lerp(gradientStrength, 0f, normalizedHeight);
                        vertexColor = Color.Lerp(vertexColor, Color.black, darken);
                    }
                    
                    vertexColors[vertexStartIndex + v] = vertexColor;
                }
                
                // Create triangles
                for (int t = 0; t < 36; t++)
                {
                    triangles[triangleStartIndex + t] = vertexStartIndex + cubeTriangles[t];
                }
            }
        }
        
        mesh.vertices = vertices;
        mesh.colors = vertexColors;
        mesh.triangles = triangles;
        
        if (recalculateNormals)
        {
            mesh.RecalculateNormals();
        }
        
        mesh.RecalculateBounds();
        
        GetComponent<MeshFilter>().mesh = mesh;
        
        Debug.Log($"Generated mesh: {vertexCount:N0} vertices, {triangleCount/3:N0} triangles");
    }

    Color CalculateCubeColor(Color pixelColor, float height)
    {
        Color result;
        
        if (usePixelColors)
        {
            result = pixelColor;
            
            // Adjust saturation
            if (saturation != 1f)
            {
                float gray = result.grayscale;
                result = Color.Lerp(new Color(gray, gray, gray, 1f), result, saturation);
            }
            
            // Apply brightness
            result *= colorBrightness;
            
            // Apply tint
            result *= tintColor;
        }
        else
        {
            result = tintColor;
        }
        
        // Apply height-based darkening for depth perception
        if (heightDarkening > 0f)
        {
            // Normalize height to 0-1 range
            float normalizedHeight = Mathf.InverseLerp(minHeight, maxHeight, height);
            
            // Calculate darkening factor (lower = darker)
            float darkenFactor = Mathf.Lerp(minDarkenedBrightness, 1f, normalizedHeight);
            darkenFactor = Mathf.Lerp(1f, darkenFactor, heightDarkening);
            
            result *= darkenFactor;
        }
        
        return result;
    }

    void Update()
    {
        if (!isPlaying) return;
        
        updateCounter++;
        
        if (isTransitioning)
        {
            // Smoothly transition to next frame
            transitionProgress += Time.deltaTime / transitionDuration;
            
            if (transitionProgress >= 1f)
            {
                // Transition complete
                transitionProgress = 1f;
                isTransitioning = false;
                holdTimer = 0f;
                
                // Snap to target values
                for (int i = 0; i < currentHeights.Length; i++)
                {
                    currentHeights[i] = targetHeights[i];
                    currentColors[i] = targetColors[i];
                }
            }
            else
            {
                // Interpolate between current and target
                float t = Mathf.SmoothStep(0f, 1f, transitionProgress);
                
                for (int i = 0; i < currentHeights.Length; i++)
                {
                    currentHeights[i] = Mathf.Lerp(currentHeights[i], targetHeights[i], t);
                    currentColors[i] = Color.Lerp(currentColors[i], targetColors[i], t);
                }
            }
            
            UpdateMesh();
        }
        else
        {
            // Holding on current frame
            holdTimer += Time.deltaTime;
            
            if (holdTimer >= frameHoldDuration)
            {
                // Time to go to next frame
                int nextFrameIndex = currentFrameIndex + 1;
                
                if (nextFrameIndex >= frames.Length)
                {
                    if (loop)
                    {
                        nextFrameIndex = 0;
                    }
                    else
                    {
                        // Animation finished
                        isPlaying = false;
                        Debug.Log("Animation finished");
                        return;
                    }
                }
                
                // Store current values as start point for interpolation
                for (int i = 0; i < currentHeights.Length; i++)
                {
                    currentHeights[i] = targetHeights[i];
                    currentColors[i] = targetColors[i];
                }
                
                // Load next frame (sets new targets)
                LoadFrame(nextFrameIndex, false);
                
                // Start transition
                isTransitioning = true;
                transitionProgress = 0f;
            }
        }
    }

    void UpdateMesh()
    {
        // Update vertex positions and colors
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int pixelIndex = y * width + x;
                int vertexStartIndex = pixelIndex * 24;
                
                Vector3 basePosition = new Vector3(x * pixelSpacing, 0, y * pixelSpacing);
                float cubeHeight = currentHeights[pixelIndex];
                Color baseCubeColor = currentColors[pixelIndex];
                
                for (int v = 0; v < 24; v++)
                {
                    Vector3 offset = cubeVertOffsets[v];
                    offset.x *= pixelSpacing;
                    offset.z *= pixelSpacing;
                    offset.y *= cubeHeight;
                    
                    vertices[vertexStartIndex + v] = basePosition + offset;
                    
                    // Apply vertical gradient if enabled
                    Color vertexColor = baseCubeColor;
                    if (useVerticalGradient && cubeHeight > 0.01f)
                    {
                        float normalizedHeight = offset.y / cubeHeight;
                        float darken = Mathf.Lerp(gradientStrength, 0f, normalizedHeight);
                        vertexColor = Color.Lerp(vertexColor, Color.black, darken);
                    }
                    
                    vertexColors[vertexStartIndex + v] = vertexColor;
                }
            }
        }
        
        mesh.vertices = vertices;
        mesh.colors = vertexColors;
        
        // Only recalculate normals every N frames
        if (recalculateNormals && updateCounter % normalUpdateInterval == 0)
        {
            mesh.RecalculateNormals();
        }
        
        mesh.RecalculateBounds();
        
        // Only update collision every N frames (if enabled)
        if (enableCollision && meshCollider != null && updateCounter % collisionUpdateInterval == 0)
        {
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = mesh;
        }
    }
    
    // Public control methods
    public void Play()
    {
        isPlaying = true;
        Debug.Log("Animation started");
    }
    
    public void Pause()
    {
        isPlaying = false;
        Debug.Log("Animation paused");
    }
    
    public void Stop()
    {
        isPlaying = false;
        isTransitioning = false;
        transitionProgress = 0f;
        holdTimer = 0f;
        LoadFrame(0, true);
        UpdateMesh();
        Debug.Log("Animation stopped");
    }
    
    public void SetFrame(int frameIndex)
    {
        bool wasPlaying = isPlaying;
        isPlaying = false;
        isTransitioning = false;
        LoadFrame(frameIndex, true);
        UpdateMesh();
        isPlaying = wasPlaying;
    }
    
    void OnValidate()
    {
        // Update collision component when toggled
        if (Application.isPlaying)
        {
            if (enableCollision && meshCollider == null)
            {
                meshCollider = gameObject.AddComponent<MeshCollider>();
                if (mesh != null)
                {
                    meshCollider.sharedMesh = mesh;
                }
            }
            else if (!enableCollision && meshCollider != null)
            {
                DestroyImmediate(meshCollider);
                meshCollider = null;
            }
        }
        
        if (mesh != null && currentColors != null && frames != null && frames.Length > 0)
        {
            Color[] pixels = GetResampledPixels(frames[currentFrameIndex]);
            for (int i = 0; i < currentColors.Length && i < pixels.Length; i++)
            {
                currentColors[i] = CalculateCubeColor(pixels[i], currentHeights[i]);
                targetColors[i] = currentColors[i];
            }
            UpdateMesh();
        }
    }
}


// ## **New Contrast Controls Explained:**

// ### **1. Height Darkening** (Most Important!)
// - **Range: 0-1**
// - Makes lower cubes darker, higher cubes brighter
// - Creates strong depth perception
// - **Recommended: 0.5-0.7** for good contrast

// ### **2. Min Darkened Brightness**
// - **Range: 0-1**
// - Prevents cubes from becoming pure black
// - **Recommended: 0.2-0.3** (keeps some detail in shadows)

// ### **3. Saturation**
// - **Range: 0-2**
// - **Below 1**: Desaturates (more gray/muted)
// - **Above 1**: Supersaturates (more vivid/neon)
// - **Recommended: 1.2-1.5** for more pop

// ### **4. Vertical Gradient**
// - **Toggle on/off**
// - Makes bottom of each cube darker than top
// - Enhances 3D look with built-in shading
// - **Recommended: ON**

// ### **5. Gradient Strength**
// - **Range: 0-1**
// - How much darker the bottom should be
// - **Recommended: 0.3-0.5**

// ## **Recommended Settings for Maximum Contrast:**

// Height Darkening: 0.6
// Min Darkened Brightness: 0.25
// Saturation: 1.3
// Use Vertical Gradient: ON
// Gradient Strength: 0.4