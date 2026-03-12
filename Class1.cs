







































































































































































































































































































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
using System.Diagnostics;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(MeshCollider))]
[RequireComponent(typeof(DecisionRequester))]
public class NNTrack : Agent
{
    [SerializeField] private Rigidbody rb;

    [Header("Lap Detection Settings")]
    public float completionRadius = 3.0f;
    public float lapReward = 1.0f;

    [Header("Lap Statistics")]
    [SerializeField] private int currentLap;
    [SerializeField] private bool readyForNewLap;
    private Vector3 startingPosition;

    [System.Serializable]
    public class RewardInfo
    {
        public float mult_forward = 0.001f;
        public float mult_barrier = -0.8f;
        public float mult_car = -0.5f;
        public float mult_backward = -0.2f;
        public float mult_speed = 0.005f;
        public float mult_noMovement = -0.1f;
        public float mult_lane = 0.2f; // Increased reward multiplier for lane following

        public float Movespeed = 30;
        public float Turnspeed = 100;
    }

    public RewardInfo rwd = new RewardInfo();

    private Vector3 recall_position;
    private Quaternion recall_rotation;

    private Bounds bnd;

    // Lane detection variables
    public Camera laneDetectionCamera;
    public RenderTexture laneRenderTexture;
    public int imageWidth = 256;
    public int imageHeight = 128;

    private Texture2D processedTexture;

    private TcpClient laneClient;
    private NetworkStream laneStream;
    private bool laneDetectorConnected = false;

    private void ConnectToLaneDetector()
    {
        Task.Run(async () =>
        {
            try
            {
                laneClient = new TcpClient();
                await laneClient.ConnectAsync("127.0.0.1", 5555);
                laneStream = laneClient.GetStream();
                laneDetectorConnected = true;
                Debug.Log("Connected to lane detector");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to connect: {e.Message}");
            }
        });
    }

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
        rb.interpolation = RigidbodyInterpolation.Extrapolate;

        GetComponent<MeshCollider>().convex = true;

        bnd = GetComponent<MeshCollider>().bounds;

        recall_position = transform.position;
        recall_rotation = transform.rotation;

        processedTexture = new Texture2D(imageWidth, imageHeight, TextureFormat.RGB24, false);

        startingPosition = transform.position;
        readyForNewLap = false;
        currentLap = 0;

        ConnectToLaneDetector();
    }

    private async void DetectLaneWithCamera()
    {
        if (!laneDetectorConnected || laneDetectionCamera == null || laneRenderTexture == null)
            return;

        try
        {
            // Ensure RenderTexture dimensions match the processed texture
            imageWidth = laneRenderTexture.width;
            imageHeight = laneRenderTexture.height;

            // Activate the RenderTexture
            RenderTexture.active = laneRenderTexture;

            // Validate the Rect dimensions and read pixels
            processedTexture.ReadPixels(new Rect(0, 0, imageWidth, imageHeight), 0, 0);
            processedTexture.Apply();

            // Reset the active RenderTexture
            RenderTexture.active = null;

            // Encode the texture to PNG
            byte[] imageBytes = processedTexture.EncodeToPNG();

            // Send the image size and data over the stream
            byte[] sizeBytes = BitConverter.GetBytes(imageBytes.Length);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(sizeBytes);

            await laneStream.WriteAsync(sizeBytes, 0, sizeBytes.Length);
            await laneStream.WriteAsync(imageBytes, 0, imageBytes.Length);

            // Read response size from the stream
            byte[] responseSizeBytes = new byte[4];
            await laneStream.ReadAsync(responseSizeBytes, 0, 4);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(responseSizeBytes);

            int responseSize = BitConverter.ToInt32(responseSizeBytes, 0);

            // Read response data from the stream
            byte[] responseBytes = new byte[responseSize];
            int totalRead = 0;
            while (totalRead < responseSize)
                totalRead += await laneStream.ReadAsync(responseBytes, totalRead, responseSize - totalRead);

            // Deserialize the JSON response
            string responseJson = Encoding.UTF8.GetString(responseBytes);

            LaneResponse responseData;
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(responseJson)))
            {
                var serializer = new DataContractJsonSerializer(typeof(LaneResponse));
                responseData = (LaneResponse)serializer.ReadObject(ms);
            }

            // Calculate and apply reward based on lane confidence
            float confidenceReward = Mathf.Lerp(-0.1f, 0.1f, responseData.lane_confidence) * rwd.mult_lane;
            AddReward(confidenceReward);

            Debug.Log($"Lane Confidence: {responseData.lane_confidence}, Reward: {confidenceReward}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Lane detection error: {e.Message}");
        }
    }

    [Serializable]
    private class LaneResponse
    {
        public float lane_confidence;
    }
    
    public override void OnActionReceived(ActionBuffers actions)
    {
        // --- Speed Reward Calculation ---
        float mag = Mathf.Abs(rb.linearVelocity.sqrMagnitude);
        float velocityDelta = Mathf.Abs(rb.linearVelocity.magnitude - rwd.Movespeed);
        float speedReward = (rwd.Movespeed - velocityDelta) * rwd.mult_speed;
        AddReward(speedReward);

        // --- Process Movement Actions ---
        switch (actions.DiscreteActions.Array[0]) // Movement
        {
            case 0: // No movement
                Debug.Log("No movement");
                AddReward(rwd.mult_noMovement);
                break;
            case 1: // Move backward
                Debug.Log("Moving backward");
                rb.AddRelativeForce(Vector3.back * rwd.Movespeed * Time.deltaTime, ForceMode.VelocityChange);
                AddReward(rwd.mult_backward);
                break;
            case 2: // Move forward
                Debug.Log("Moving forward");
                rb.AddRelativeForce(Vector3.forward * rwd.Movespeed * Time.deltaTime, ForceMode.VelocityChange);
                AddReward(mag * rwd.mult_forward);
                break;
        }

        switch (actions.DiscreteActions.Array[1]) // Turning
        {
            case 0: // No turning
                Debug.Log("No turning");
                break;
            case 1: // Turn left
                Debug.Log("Turning left");
                transform.Rotate(Vector3.up, -rwd.Turnspeed * Time.deltaTime);
                break;
            case 2: // Turn right
                Debug.Log("Turning right");
                transform.Rotate(Vector3.up, rwd.Turnspeed * Time.deltaTime);
                break;
        }

        // --- Check Lap Completion ---

        // --- Lane Detection and Reward ---
        DetectLaneWithCamera();

        // --- Collision Penalty (if applicable) ---
        if (rb.linearVelocity.magnitude < 0.1f)
        {
            AddReward(rwd.mult_noMovement); // Penalty for being stationary
            Debug.LogWarning("Agent is not moving!");
        }
    }
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        // For manual control during testing
        actionsOut.DiscreteActions.Array[0] = 0;
        actionsOut.DiscreteActions.Array[1] = 0;
        float move = Input.GetAxis("Vertical");
        float turn = Input.GetAxis("Horizontal");
        Debug.Log($"Move Input: {move}, Turn Input: {turn}");

        if (move < 0)
            actionsOut.DiscreteActions.Array[0] = 1; // backward
        else if (move > 0)
            actionsOut.DiscreteActions.Array[0] = 2; // forward
        if (turn < 0)
            actionsOut.DiscreteActions.Array[1] = 1; // left
        else if (turn > 0)
            actionsOut.DiscreteActions.Array[1] = 2; // right
    }

    // Cleanup resources
    private void OnDestroy()
    {
        if (laneClient != null)
            laneClient.Close();
    }
}






using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using System;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(MeshCollider))]
[RequireComponent(typeof(DecisionRequester))]

public class NNTrack : Agent
{
    [SerializeField]
    public Rigidbody rb = null;
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
    }

    public float Movespeed = 30;
    public float Turnspeed = 100;
    public RewardInfo rwd = new RewardInfo();
    public bool doEpisodes = true;
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

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
        rb.linearDamping = 1;
        rb.angularDamping = 5;
        rb.interpolation = RigidbodyInterpolation.Extrapolate;
        GetComponent<MeshCollider>().convex = true;
        GetComponent<DecisionRequester>().DecisionPeriod = 1;
        bnd = GetComponent<MeshCollider>().bounds;
        recall_position = transform.position;
        recall_rotation = transform.rotation;

        // Create a Texture2D to process the camera image
        processedTexture = new Texture2D(imageWidth, imageHeight, TextureFormat.RGB24, false);


    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // Add a basic observation (e.g., the agent's local x position)
        sensor.AddObservation(transform.localPosition.x);
        sensor.AddObservation(rb.linearVelocity.normalized); // Adds x, y, and z components
        sensor.AddObservation(lastLaneConfidence);

        // Angular velocity (normalized)
        sensor.AddObservation(rb.angularVelocity.normalized); // Adds x, y, and z components
        // Note: Although visual observations are usually provided via a CameraSensor,
        // here we use the camera output only for reward shaping and debug visualization.
    }

    public override void OnEpisodeBegin()
    {
        rb.linearVelocity = Vector3.zero;
        transform.position = recall_position;
        transform.rotation = recall_rotation;
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // Speed reward calculation based on deviation from target speed
        float mag = Mathf.Abs(rb.linearVelocity.sqrMagnitude);
        float velocityDelta = Mathf.Abs(rb.linearVelocity.magnitude - targetSpeed);
        float speedReward = (targetSpeed - velocityDelta) * rwd.mult_speed;
        AddReward(speedReward);

        // Process movement actions
        switch (actions.DiscreteActions.Array[0]) // Movement
        {
            case 0:
                Debug.Log("No movement");
                AddReward(rwd.mult_noMovement);
                break;
            case 1:
                Debug.Log("Moving backward");
                rb.AddRelativeForce(Vector3.up * Movespeed * Time.deltaTime, ForceMode.VelocityChange);
                AddReward(rwd.mult_backward);
                break;
            case 2:
                Debug.Log("Moving forward");
                rb.AddRelativeForce(Vector3.down * Movespeed * Time.deltaTime, ForceMode.VelocityChange);
                AddReward(mag * rwd.mult_forward);
                break;
        }

        switch (actions.DiscreteActions.Array[1]) // Turning
        {
            case 0:
                Debug.Log("No turning");
                break;
            case 1:
                Debug.Log("Turning left");
                transform.Rotate(Vector3.forward, -Turnspeed * Time.deltaTime);
                break;
            case 2:
                Debug.Log("Turning right");
                transform.Rotate(Vector3.forward, Turnspeed * Time.deltaTime);
                break;
        }
    }


    public override void Heuristic(in ActionBuffers actionsOut)
    {
        // For manual control during testing
        actionsOut.DiscreteActions.Array[0] = 0;
        actionsOut.DiscreteActions.Array[1] = 0;
        float move = Input.GetAxis("Vertical");
        float turn = Input.GetAxis("Horizontal");
        Debug.Log($"Move Input: {move}, Turn Input: {turn}");

        if (move < 0)
            actionsOut.DiscreteActions.Array[0] = 1; // backward
        else if (move > 0)
            actionsOut.DiscreteActions.Array[0] = 2; // forward
        if (turn < 0)
            actionsOut.DiscreteActions.Array[1] = 1; // left
        else if (turn > 0)
            actionsOut.DiscreteActions.Array[1] = 2; // right
    }

    private void OnCollisionEnter(Collision collision)
    {
        float mag = collision.relativeVelocity.sqrMagnitude;
        if (collision.gameObject.CompareTag("BarrierWhite") ||
            collision.gameObject.CompareTag("BarrierYellow"))
        {
            AddReward(mag * rwd.mult_barrier);
            if (doEpisodes)
                EndEpisode();
        }
        else if (collision.gameObject.CompareTag("Car"))
        {
            AddReward(mag * rwd.mult_car);
            if (doEpisodes)
                EndEpisode();
        }
    }
    // This method processes the camera�s render texture, calculates a lane confidence value,
    // and maps that into a reward. It also stores the confidence for visual inspection.
    private void DetectLaneWithCamera()
    {
        if (laneDetectionCamera == null || laneRenderTexture == null)
        {
            Debug.LogWarning("Lane detection camera or render texture is not assigned!");
            return;
        }

        // Ensure Render Texture dimensions match
        imageWidth = laneRenderTexture.width;
        imageHeight = laneRenderTexture.height;

        // Activate the render texture and read pixels into processed texture
        RenderTexture.active = laneRenderTexture;

        try
        {
            processedTexture.ReadPixels(new Rect(0, 0, imageWidth, imageHeight), 0, 0);
            processedTexture.Apply();
        }
        catch (Exception e)
        {
            Debug.LogError($"Error reading pixels from Render Texture: {e.Message}");
            return;
        }

        // Get the center pixel of the processed texture
        Color centerPixel = processedTexture.GetPixel(imageWidth / 2, imageHeight / 2);

        // Use grayscale value as confidence measure (assumes bright lane markings)
        lastLaneConfidence = Mathf.Clamp01(centerPixel.grayscale);

        // Map confidence value into a reward between -0.1 and 0.1
        float laneReward = Mathf.Lerp(-0.1f, 0.1f, lastLaneConfidence) * rwd.mult_lane;
        AddReward(laneReward);

        // Debug log values
        Debug.Log($"Lane Reward: {laneReward} | Lane Confidence: {lastLaneConfidence}");
    }
}