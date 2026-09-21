using UnityEngine;
using System.Collections; // Aggiunto per poter usare le Coroutine

public class TrackingModeManager : MonoBehaviour
{
    [Header("Seleziona la Modalità")]
    public bool useSimulation = true;

    [Header("I due Sistemi")]
    public GameObject liveOVRBody;
    public GameObject simulatedOVRBody;

    private bool lastState;

    void Start()
    {
        lastState = useSimulation;
        // Invece di spegnere tutto subito, aspetto che Meta abbia finito di caricare
        StartCoroutine(InitWithDelay());
    }

    private IEnumerator InitWithDelay()
    {
        // Aspetta letteralmente un frame
        yield return new WaitForEndOfFrame();
        SwitchMode();
    }

    void Update()
    {
        if (useSimulation != lastState)
        {
            SwitchMode();
            lastState = useSimulation;
        }
    }

    private void SwitchMode()
    {
        if (liveOVRBody == null || simulatedOVRBody == null) return;

        if (useSimulation)
        {
            liveOVRBody.SetActive(false);
            simulatedOVRBody.SetActive(true);
            Debug.Log("Tracking SIMULATO Attivo");
        }
        else
        {
            simulatedOVRBody.SetActive(false);
            liveOVRBody.SetActive(true);
            Debug.Log("Tracking LIVE Attivo");
        }
    }
}