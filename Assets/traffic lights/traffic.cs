using UnityEngine;
using System.Collections;

public class TrafficLightController : MonoBehaviour
{
    // Assign the renderers for each light in the Inspector
    public Renderer redLight;
    public Renderer yellowLight;
    public Renderer greenLight;

    // Time for each light (in seconds)
    public float cycleTime = 10f;

    private Material redMaterial;
    private Material yellowMaterial;
    private Material greenMaterial;

    private void Start()
    {
        // Get materials from the renderers
        redMaterial = redLight.material;
        yellowMaterial = yellowLight.material;
        greenMaterial = greenLight.material;

        // Start the traffic light cycle
        StartCoroutine(TrafficCycle());
    }

    private IEnumerator TrafficCycle()
    {
        while (true)
        {
            // Red light on
            EnableEmission(redMaterial, Color.red);
            EnableEmission(yellowMaterial, Color.black); // Turn off yellow
            EnableEmission(greenMaterial, Color.black);  // Turn off green
            yield return new WaitForSeconds(cycleTime);

            // Green light on
            EnableEmission(redMaterial, Color.black);   // Turn off red
            EnableEmission(yellowMaterial, Color.black); // Turn off yellow
            EnableEmission(greenMaterial, Color.green);
            yield return new WaitForSeconds(cycleTime);

            // Yellow light on
            EnableEmission(redMaterial, Color.black);   // Turn off red
            EnableEmission(yellowMaterial, Color.yellow);
            EnableEmission(greenMaterial, Color.black);  // Turn off green
            yield return new WaitForSeconds(cycleTime / 2); // Shorter time for yellow
        }
    }

    private void EnableEmission(Material material, Color color)
    {
        if (color != Color.black)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 2f); // Adjust intensity as needed
        }
        else
        {
            material.DisableKeyword("_EMISSION");
        }
    }
}
