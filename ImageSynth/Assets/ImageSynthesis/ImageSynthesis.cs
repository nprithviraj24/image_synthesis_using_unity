using UnityEngine;
using UnityEngine.Rendering;
using System.Collections;
using System.IO;

// @TODO:
// . support custom color wheels in optical flow via lookup textures
// . support custom depth encoding
// . support multiple overlay cameras
// . tests
// . better example scene(s)

// @KNOWN ISSUES
// . Motion Vectors can produce incorrect results in Unity 5.5.f3 when
//      1) during the first rendering frame
//      2) rendering several cameras with different aspect ratios - vectors do stretch to the sides of the screen

[RequireComponent (typeof(Camera))]
public class ImageSynthesis : MonoBehaviour {

	// pass configuration
	private CapturePass[] capturePasses = new CapturePass[] {
		new CapturePass() { name = "_img" },
		new CapturePass() { name = "_id", supportsAntialiasing = false },
		new CapturePass() { name = "_layer", supportsAntialiasing = false },
		new CapturePass() { name = "_depth" },
		new CapturePass() { name = "_normals" },
		new CapturePass() { name = "_flow", supportsAntialiasing = false, needsRescale = true } // (see issue with Motion Vectors in @KNOWN ISSUES)
	};

	struct CapturePass {
		// configuration
		public string name;
		public bool supportsAntialiasing;
		public bool needsRescale;
		public CapturePass(string name_) { name = name_; supportsAntialiasing = true; needsRescale = false; camera = null; }

		// impl
		public Camera camera;
	};
	
	public Shader uberReplacementShader;
	public Shader opticalFlowShader;

	public float opticalFlowSensitivity = 1.0f;

	// cached materials
	private Material opticalFlowMaterial;

	void Start()
	{
		// default fallbacks, if shaders are unspecified
		if (!uberReplacementShader)
			uberReplacementShader = Shader.Find("Hidden/UberReplacement");

		if (!opticalFlowShader)
			opticalFlowShader = Shader.Find("Hidden/OpticalFlow");

		// use real camera to capture final image
		capturePasses[0].camera = GetComponent<Camera>();
		for (int q = 1; q < capturePasses.Length; q++)
			capturePasses[q].camera = CreateHiddenCamera (capturePasses[q].name);

		OnCameraChange();
		OnSceneChange();
	}

	void LateUpdate()
	{
		#if UNITY_EDITOR
		if (DetectPotentialSceneChangeInEditor())
			OnSceneChange();
		#endif // UNITY_EDITOR

		// @TODO: detect if camera properties actually changed
		OnCameraChange();
	}
	
	private Camera CreateHiddenCamera(string name)
	{
		var go = new GameObject (name, typeof (Camera));
		go.hideFlags = HideFlags.HideAndDontSave;
		go.transform.parent = transform;

		var newCamera = go.GetComponent<Camera>();
		return newCamera;
	}


	static private void SetupCameraWithReplacementShader(Camera cam, Shader shader, ReplacelementModes mode)
	{
		SetupCameraWithReplacementShader(cam, shader, mode, Color.black);
	}

	static private void SetupCameraWithReplacementShader(Camera cam, Shader shader, ReplacelementModes mode, Color clearColor)
	{
		var cb = new CommandBuffer();
		cb.SetGlobalFloat("_OutputMode", (int)mode); // @TODO: CommandBuffer is missing SetGlobalInt() method
		cam.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, cb);
		cam.AddCommandBuffer(CameraEvent.BeforeFinalPass, cb);
		cam.SetReplacementShader(shader, "");
		cam.backgroundColor = clearColor;
		cam.clearFlags = CameraClearFlags.SolidColor;
	}

	static private void SetupCameraWithPostShader(Camera cam, Material material, DepthTextureMode depthTextureMode = DepthTextureMode.None)
	{
		var cb = new CommandBuffer();
		cb.Blit(null, BuiltinRenderTextureType.CurrentActive, material);
		cam.AddCommandBuffer(CameraEvent.AfterEverything, cb);
		cam.depthTextureMode = depthTextureMode;
	}

	enum ReplacelementModes {
		ObjectId 			= 0,
		CatergoryId			= 1,
		DepthCompressed		= 2,
		DepthMultichannel	= 3,
		Normals				= 4
	};

	public void OnCameraChange()
	{
		int targetDisplay = 1;
		var mainCamera = GetComponent<Camera>();
		foreach (var pass in capturePasses)
		{
			if (pass.camera == mainCamera)
				continue;

			// cleanup capturing camera
			pass.camera.RemoveAllCommandBuffers();

			// copy all "main" camera parameters into capturing camera
			pass.camera.CopyFrom(mainCamera);

			// set targetDisplay here since it gets overriden by CopyFrom()
			pass.camera.targetDisplay = targetDisplay++;
		}

		// cache materials and setup material properties
		if (!opticalFlowMaterial || opticalFlowMaterial.shader != opticalFlowShader)
			opticalFlowMaterial = new Material(opticalFlowShader);
		opticalFlowMaterial.SetFloat("_Sensitivity", opticalFlowSensitivity);

		// setup command buffers and replacement shaders
		SetupCameraWithReplacementShader(capturePasses[1].camera, uberReplacementShader, ReplacelementModes.ObjectId);
		SetupCameraWithReplacementShader(capturePasses[2].camera, uberReplacementShader, ReplacelementModes.CatergoryId);
		SetupCameraWithReplacementShader(capturePasses[3].camera, uberReplacementShader, ReplacelementModes.DepthCompressed, Color.white);
		SetupCameraWithReplacementShader(capturePasses[4].camera, uberReplacementShader, ReplacelementModes.Normals);
		SetupCameraWithPostShader(capturePasses[5].camera, opticalFlowMaterial, DepthTextureMode.Depth | DepthTextureMode.MotionVectors);
	}


	public void OnSceneChange()
	{
		var renderers = Object.FindObjectsOfType<Renderer>();
		var mpb = new MaterialPropertyBlock();
		var cam = GetComponent<Camera>();
		foreach (var r in renderers)
		{
			var id = r.gameObject.GetInstanceID();
			var layer = r.gameObject.layer;
			var tag = r.gameObject.tag;
            var tf = r.gameObject.transform.position;
			var sc = r.gameObject.transform.localScale;
			var rot = r.gameObject.transform.rotation;
			Debug.Log("Rotation: "+ rot);
			// var bnd = r.gameObject.GetComponent<Renderer>().bounds.size;
			var MESHbounds = r.gameObject.GetComponent<MeshFilter>().mesh.bounds.size;
			var meshBounds = r.gameObject.GetComponent<MeshFilter>().mesh.bounds;
			// float width = GetComponent<SpriteRenderer>().bounds.size.x;
			Mesh mesh = r.gameObject.GetComponent<MeshFilter>().mesh;
            Vector3[] vertices = mesh.vertices;
            // Vector2[] uvs = new Vector2[vertices.Length];
            Bounds bounds = mesh.bounds;
        	int i = 0;
			// float tfX = tf.x*sc.x;
			// float tfY = tf.y*sc.y;
			// float tfZ = tf.z*sc.z;

			// Vector3 cam_tf = (rot * new Vector3(tfX, tfY, tfZ) ) ;
			Vector3 mid_pt = cam.WorldToScreenPoint(tf);
			File.AppendAllText(@"/home/prithvi/Desktop/random.csv", "\nimage_00000_img.png," + (int)mid_pt.x +", "+(int)(512-mid_pt.y) );
        	while (i < vertices.Length) //https://docs.unity3d.com/ScriptReference/MeshFilter-mesh.html
        	{
        	    // uvs[i] = new Vector2(vertices[i].x / bounds.size.x, vertices[i].z / bounds.size.x);
            	// Vector3 screenPos = cam.WorldToScreenPoint(tf);

				// if (System.Math.Abs(vertices[i].x) == bounds.extents.x | System.Math.Abs(vertices[i].y) == bounds.extents.y |  System.Math.Abs(vertices[i].z) == bounds.extents.z ){
					float scX = vertices[i].x*sc.x;
					float scY = vertices[i].y*sc.y;
					float scZ = vertices[i].z*sc.z;

					Vector3 change = tf+ (rot * new Vector3(scX, scY, scZ) ) ;
					Vector3 offset = cam.WorldToScreenPoint(change);

					// float bx1X = screenPos.x + offset.x;
					// float bx1Y = System.Math.Abs(512 - screenPos.y) + System.Math.Abs(512 - offset.y);
					float bx1X = offset.x;
					float bx1Y = System.Math.Abs(512 - offset.y);

        			File.AppendAllText(@"/home/prithvi/Desktop/random.csv", ","+(int)bx1X+","+(int)bx1Y);
				// }
					i++;
        	}


        	// File.AppendAllText(@"/home/prithvi/Desktop/random.csv", "\n");
        	// mesh.uv = uvs;

            // Vector3 screenPos = cam.WorldToScreenPoint(tf);
			// Vector3 minus = tf-MESHbounds;
			// Vector3 plus = tf+MESHbounds;
            // Vector3 pad1 = cam.WorldToScreenPoint(minus);
            // Vector3 pad2 = cam.WorldToScreenPoint(plus);
            // Vector3 scale= cam.WorldToScreenPoint(test);
            // var abc = r.gameObject.GetMaterials();
			// float bx1X = screenPos.x - test.x/2;
			// float bx1Y = System.Math.Abs(512 - screenPos.y) - test.y/2;
			// float bx2X = screenPos.x + test.x/2;
			// float bx2Y = System.Math.Abs(512 - screenPos.y) + test.y/2;
			// Debug.Log("Bounding Boxes: ("+bx1X+","+bx1Y+") and ("+ bx2X+","+bx2Y+")");
			// string line = "Bounding Boxes: ("+pad1.x+","+System.Math.Abs(512f-pad1.y)+") and ("+ pad2.x+","+System.Math.Abs(512f-pad2.y)+")";
			// line +=  " \n target is (" + screenPos.x +"," + System.Math.Abs(512-screenPos.y)  + ") pixels from the left for "+tf.ToString(); 
        	// File.AppendAllText(@"/home/prithvi/Desktop/random.txt", line + "\n");
			// Debug.Logline);
			// Debug.Log(vertices.Length+ " ..... "+ vertices[0]+ "");

			Debug.Log("Min: " +bounds.min + "   Max: "+ bounds.max+ "   Center: "+ bounds.center+ " Extents: "+bounds.extents);
			mpb.SetColor("_ObjectColor", ColorEncoding.EncodeIDAsColor(id));
			mpb.SetColor("_CategoryColor", ColorEncoding.EncodeLayerAsColor(layer));
			r.SetPropertyBlock(mpb);
		}
		// Debug.Log((Object.FindObjectsOfType<GameObject>()).ToString());
		// Scene scene = Object.FindObjectsOfType<GameObject>().scene;
        // Debug.Log("  NAME: "+ Object.FindObjectsOfType<GameObject>().ToString());
	}

	public void Save(string filename, int width = -1, int height = -1, string path = "")
	{
		if (width <= 0 || height <= 0)
		{
			width = Screen.width;
			height = Screen.height;
		}

		var filenameExtension = System.IO.Path.GetExtension(filename);
		if (filenameExtension == "")
			filenameExtension = ".png";
		var filenameWithoutExtension = Path.GetFileNameWithoutExtension(filename);

		var pathWithoutExtension = Path.Combine(path, filenameWithoutExtension);

		// execute as coroutine to wait for the EndOfFrame before starting capture
		StartCoroutine(
			WaitForEndOfFrameAndSave(pathWithoutExtension, filenameExtension, width, height));
	}

	private IEnumerator WaitForEndOfFrameAndSave(string filenameWithoutExtension, string filenameExtension, int width, int height)
	{
		yield return new WaitForEndOfFrame();
		Save(filenameWithoutExtension, filenameExtension, width, height);
	}

	private void Save(string filenameWithoutExtension, string filenameExtension, int width, int height)
	{
		foreach (var pass in capturePasses)
			Save(pass.camera, filenameWithoutExtension + pass.name + filenameExtension, width, height, pass.supportsAntialiasing, pass.needsRescale);
	}

	private void Save(Camera cam, string filename, int width, int height, bool supportsAntialiasing, bool needsRescale)
	{
		var mainCamera = GetComponent<Camera>();
		var depth = 24;
		var format = RenderTextureFormat.Default;
		var readWrite = RenderTextureReadWrite.Default;
		var antiAliasing = (supportsAntialiasing) ? Mathf.Max(1, QualitySettings.antiAliasing) : 1;

		var finalRT =
			RenderTexture.GetTemporary(width, height, depth, format, readWrite, antiAliasing);
		var renderRT = (!needsRescale) ? finalRT :
			RenderTexture.GetTemporary(mainCamera.pixelWidth, mainCamera.pixelHeight, depth, format, readWrite, antiAliasing);
		var tex = new Texture2D(width, height, TextureFormat.RGB24, false);

		var prevActiveRT = RenderTexture.active;
		var prevCameraRT = cam.targetTexture;
		
		// Debug.Log(mainCamera);

		// render to offscreen texture (readonly from CPU side)
		RenderTexture.active = renderRT;
		cam.targetTexture = renderRT;

		cam.Render();

		if (needsRescale)
		{
			// blit to rescale (see issue with Motion Vectors in @KNOWN ISSUES)
			RenderTexture.active = finalRT;
			Graphics.Blit(renderRT, finalRT);
			RenderTexture.ReleaseTemporary(renderRT);
		}

		// read offsreen texture contents into the CPU readable texture
		tex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
		tex.Apply();

		// encode texture into PNG
		var bytes = tex.EncodeToPNG();
		File.WriteAllBytes(filename, bytes);					

		// restore state and cleanup
		cam.targetTexture = prevCameraRT;
		RenderTexture.active = prevActiveRT;

		Object.Destroy(tex);
		RenderTexture.ReleaseTemporary(finalRT);
	}

	#if UNITY_EDITOR
	private GameObject lastSelectedGO;
	private int lastSelectedGOLayer = -1;
	private string lastSelectedGOTag = "unknown";
	private bool DetectPotentialSceneChangeInEditor()
	{
		bool change = false;
		// there is no callback in Unity Editor to automatically detect changes in scene objects
		// as a workaround lets track selected objects and check, if properties that are 
		// interesting for us (layer or tag) did not change since the last frame
		if (UnityEditor.Selection.transforms.Length > 1)
		{
			// multiple objects are selected, all bets are off!
			// we have to assume these objects are being edited
			change = true;
			lastSelectedGO = null;
		}
		else if (UnityEditor.Selection.activeGameObject)
		{
			var go = UnityEditor.Selection.activeGameObject;
			// check if layer or tag of a selected object have changed since the last frame
			var potentialChangeHappened = lastSelectedGOLayer != go.layer || lastSelectedGOTag != go.tag;
			if (go == lastSelectedGO && potentialChangeHappened)
				change = true;

			lastSelectedGO = go;
			lastSelectedGOLayer = go.layer;
			lastSelectedGOTag = go.tag;
		}

		return change;
	}
	#endif // UNITY_EDITOR
}