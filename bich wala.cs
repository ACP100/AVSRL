
// Add these using statements
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Serialization.Json;
using System.IO;
using System;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(MeshCollider))]
[RequireComponent(typeof(DecisionRequester))]
public class NNTrack : Agent
{
    [SerializeField]
    private Rigidbody rb;

    private float targetSpeed = 12f;

   

    [System.Serializable]
    public class RewardInfo
    {
        public float mult_forward = 0.001f;
        public float mult_barrier = -0.8f;
        public float mult_car = -0.5f;
        public float mult_backward = -0.2f;
        public float mult_speed = 0.005f;
        public float mult_noMovement = -0.1f;
        public float mult_lane = 0.1f;

        // Movement settings
        public float Movespeed = 30;
        public float Turnspeed = 100;
    }

    public RewardInfo rwd = new RewardInfo();

    private Vector3 recall_position;
    private Quaternion recall_rotation;

    private Bounds bnd;


    // --- CAMERA LANE DETECTION VARIABLES ---
    [Tooltip("The camera used for lane detection.")]
    public Camera laneDetectionCamera;

    [Tooltip("The render texture to read from.")]
    public RenderTexture laneRenderTexture;

    [Tooltip("The width of the processed image.")]
    public int imageWidth = 256;

    [Tooltip("The height of the processed image.")]
    public int imageHeight = 128;

    // Cached Texture2D for image processing
    private Texture2D processedTexture;

    // Variable to store the latest lane confidence (0 to 1)
    private float lastLaneConfidence = 0f;

    // TCP Connection Variables for Lane Detection
    private TcpClient laneClient;
    private NetworkStream laneStream;
    private bool laneDetectorConnected = false;

    // Initialize method
    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
        rb.interpolation = RigidbodyInterpolation.Extrapolate;

        GetComponent<MeshCollider>().convex = true;

        bnd = GetComponent<MeshCollider>().bounds;

        recall_position = transform.position;
        recall_rotation = transform.rotation;

        // Create a Texture2D to process the camera image
        processedTexture = new Texture2D(imageWidth, imageHeight, TextureFormat.RGB24, false);

        

        // Connect to lane detector
        ConnectToLaneDetector();
    }

    // Connect to Python-based Lane Detector
    private async void ConnectToLaneDetector()
    {
        try
        {
            laneClient = new TcpClient();
            await laneClient.ConnectAsync("127.0.0.1", 5555);
            laneStream = laneClient.GetStream();
            laneDetectorConnected = true;
            Debug.Log("Connected to lane detector");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to connect to lane detector: {e.Message}");
        }
    }

    // Detect Lane using Camera and Python Server
    private async void DetectLaneWithCamera()
    {
        if (!laneDetectorConnected || laneDetectionCamera == null || laneRenderTexture == null)
        {
            lastLaneConfidence = 0f;
            return;
        }

        try
        {
            // Activate the render texture and read the pixels into our processedTexture
            RenderTexture.active = laneRenderTexture;
            processedTexture.ReadPixels(new Rect(0, 0, imageWidth, imageHeight), 0, 0);
            processedTexture.Apply();

            // Convert texture to bytes
            byte[] imageBytes = processedTexture.EncodeToPNG();

            // Send image size first
            byte[] sizeBytes = BitConverter.GetBytes(imageBytes.Length);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(sizeBytes);
            await laneStream.WriteAsync(sizeBytes, 0, sizeBytes.Length);

            // Send image data
            await laneStream.WriteAsync(imageBytes, 0, imageBytes.Length);

            // Read response size
            byte[] responseSizeBytes = new byte[4];
            await laneStream.ReadAsync(responseSizeBytes, 0, 4);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(responseSizeBytes);
            int responseSize = BitConverter.ToInt32(responseSizeBytes, 0);

            // Read response
            byte[] responseBytes = new byte[responseSize];
            await laneStream.ReadAsync(responseBytes, 0, responseSize);
            string responseJson = Encoding.UTF8.GetString(responseBytes);

            // Parse JSON response
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(responseJson)))
            {
                var serializer = new DataContractJsonSerializer(typeof(LaneResponse));
                var response = (LaneResponse)serializer.ReadObject(ms);
                lastLaneConfidence = response.lane_confidence;
            }

            // Map the confidence value into a reward between -0.1 and 0.1
            float laneReward = Mathf.Lerp(-0.1f, 0.1f, lastLaneConfidence) * rwd.mult_lane;
            AddReward(laneReward);

            Debug.Log($"Lane Reward: {laneReward} | Lane Confidence: {lastLaneConfidence}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Lane detection error: {e.Message}");
            lastLaneConfidence = 0f;
        }
    }

    // JSON Deserialization Class for Lane Detection Response
    [System.Serializable]
    private class LaneResponse
    {
        public float lane_confidence;
    }

    // Cleanup on Destroy
    private void OnDestroy()
    {
        if (laneClient != null)
        {
            laneClient.Close();
        }
    }
}
*/