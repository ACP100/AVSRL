using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Runtime.InteropServices.ComTypes;
using System.Diagnostics;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(MeshCollider))]
[RequireComponent(typeof(DecisionRequester))]
public class NNTrack : Agent
{
    [SerializeField] private Rigidbody rb;
    [System.Serializable]
    public class LaneDetectionResponse
    {
        public float lane_confidence;
    }

    [System.Serializable]
    public class RewardInfo
    {
        public float mult_forward = 0.001f;
        public float mult_barrier = -0.8f;
        public float mult_car = -0.5f;
        public float mult_backward = -0.2f;
        public float mult_speed = 0.005f;
        public float mult_noMovement = -0.1f;
        public float mult_lane = 0.2f; 
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

    private void LogReward(string reason, float reward)
    {
        UnityEngine.Debug.Log($"Reward Applied: {reward} | Reason: {reason}");
    }

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
                
                UnityEngine.Debug.Log("Connected to lane detector");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"Failed to connect: {e.Message}");
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


        ConnectToLaneDetector();
    }
    
    private async void DetectLaneWithCamera()
    {
        if (!laneDetectorConnected || laneDetectionCamera == null || laneRenderTexture == null)
            {
                UnityEngine.Debug.LogWarning("Lane detection not ready");
                return;
            }


        try
        {
            // Convert Render Texture to Texture2D
            Texture2D texture2D = new Texture2D(laneRenderTexture.width, laneRenderTexture.height, TextureFormat.RGBA32, false);
            RenderTexture.active = laneRenderTexture;
            texture2D.ReadPixels(new Rect(0, 0, laneRenderTexture.width, laneRenderTexture.height), 0, 0);
            texture2D.Apply();
            RenderTexture.active = null;


            //SEND
            // Encode Texture2D to PNG bytes
            byte[] imageBytes = texture2D.EncodeToPNG();

            // Send image size first (4 bytes)
            byte[] sizeBytes = BitConverter.GetBytes(imageBytes.Length);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(sizeBytes); // Ensure big-endian format

            laneStream.Write(sizeBytes, 0, sizeBytes.Length);

            // Send image data
            laneStream.Write(imageBytes, 0, imageBytes.Length);
            //Debug.Log("Image sent to Python server.");


            //RECIEVE
            // Step 1: Read the length of the incoming message (4 bytes)
            byte[] lengthBytes = new byte[4];
            laneStream.Read(lengthBytes, 0, lengthBytes.Length);


            int messageLength = (lengthBytes[0] << 24) | (lengthBytes[1] << 16) | (lengthBytes[2] << 8) | lengthBytes[3];
            //if (BitConverter.IsLittleEndian)
            //    Array.Reverse(lengthBytes); // Convert from big-endian

            //int messageLength = BitConverter.ToInt32(lengthBytes.Reverse().ToArray(), 0);

            // int messageLength = BitConverter.ToInt32(lengthBytes, 0);

            // Step 2: Read the actual message based on its length
            byte[] messageBytes = new byte[messageLength];
            int bytesRead = laneStream.Read(messageBytes, 0, messageLength);

            string message = Encoding.UTF8.GetString(messageBytes);

            // Debug.Log($"Message received from Python: {message}");

            if (message == "Lane detection complete")
            {
                UnityEngine.Debug.Log("Python has completed lane detection.");
            }




            // Read response size from the stream
            //byte[] responseSizeBytes = new byte[4];
            //await laneStream.ReadAsync(responseSizeBytes, 0, 4);
            //if (BitConverter.IsLittleEndian)
            //    Array.Reverse(responseSizeBytes);

            //int responseSize = BitConverter.ToInt32(responseSizeBytes, 0);

            //// Read response data from the stream
            //byte[] responseBytes = new byte[responseSize];
            //int totalRead = 0;
            //while (totalRead < responseSize)
            //    totalRead += await laneStream.ReadAsync(responseBytes, totalRead, responseSize - totalRead);

            //// Deserialize the JSON response
            //string responseJson = Encoding.UTF8.GetString(responseBytes);

            //    LaneDetectionResponse responseData = JsonUtility.FromJson<LaneDetectionResponse>(responseJson);
            byte[] confidenceBytes = new byte[4]; // A float is 4 bytes
            await laneStream.ReadAsync(confidenceBytes, 0, confidenceBytes.Length);
            // Convert the bytes to a float
            float confidence_actual = BitConverter.ToSingle(confidenceBytes, 0);

            if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(confidenceBytes); // Convert to big-endian
                    confidence_actual = BitConverter.ToSingle(confidenceBytes, 0);
                }

                // Calculate and apply reward based on lane confidence
                float confidenceReward = Mathf.Lerp(-0.1f, 0.1f, confidence_actual) * rwd.mult_lane;
        AddReward(confidenceReward);
            LogReward("Lane Following", confidenceReward);

            
            //UnityEngine.Debug.Log($"Lane Confidence: {confidence_actual}, Reward: {confidenceReward}");
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"Lane detection error: {e.Message}");
        }
    }

    public override void OnEpisodeBegin()
    {
        transform.position = recall_position;
        transform.rotation = recall_rotation;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    public override void OnActionReceived(ActionBuffers actions)
{
    float mag = Mathf.Abs(rb.linearVelocity.sqrMagnitude);

    // Movement Actions
    switch (actions.DiscreteActions.Array[0])
    {
        case 0:
            //Debug.Log("No movement");
            AddReward(rwd.mult_noMovement);
            LogReward("no movement" , rwd.mult_noMovement);
            break;
        case 1:
            //Debug.Log("Moving backward");
            rb.AddRelativeForce(Vector3.up * rwd.Movespeed * Time.deltaTime, ForceMode.VelocityChange);
            AddReward(rwd.mult_backward);
            LogReward("backward" , rwd.mult_backward);
                break;
        case 2:
            //Debug.Log("Moving forward");
            rb.AddRelativeForce(Vector3.down * rwd.Movespeed * Time.deltaTime, ForceMode.VelocityChange);
            AddReward(mag * rwd.mult_forward);
                LogReward("moving forward", rwd.mult_forward);
                break;
    }

    // Turning Actions
    switch (actions.DiscreteActions.Array[1])
    {
        case 0:
            //Debug.Log("No turning");
            break;
        case 1:
            //Debug.Log("Turning left");
            transform.Rotate(Vector3.forward, -rwd.Turnspeed * Time.deltaTime);
            break;
        case 2:
            //Debug.Log("Turning right");
            transform.Rotate(Vector3.forward, rwd.Turnspeed * Time.deltaTime);
            break;
    }

    // Call lane detection for reward shaping
    DetectLaneWithCamera();

    }
    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Barrier"))
        {
            UnityEngine.Debug.Log("Collided with Barrier!");
            AddReward(rwd.mult_barrier);
            LogReward("Collision with Barrier", rwd.mult_barrier);

            EndEpisode(); // Optional: Reset episode after collision
        }
        else if (collision.gameObject.CompareTag("Car"))
        {
            UnityEngine.Debug.Log("Collided with another Car!");
            AddReward(rwd.mult_car);
            LogReward("Collision with car", rwd.mult_car);
            EndEpisode(); // Optional: Reset episode after collision
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
{
    actionsOut.DiscreteActions.Array[0] = 0;
    actionsOut.DiscreteActions.Array[1] = 0;

    float move = Input.GetAxis("Vertical");
    float turn = Input.GetAxis("Horizontal");

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








































































































































































































































































































































































































