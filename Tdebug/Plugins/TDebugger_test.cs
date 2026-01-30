using UnityEngine;
using System.Collections;
using TDebugger;

public class TDebugger_test : MonoBehaviour 
{
	// Use this for initialization
	void Start () 
    {
        TDebug.Toggle = true;
        TDebug.Log(string.Format("TDebugger.TDebug.Log(),TDebug.Toggle = {0}", TDebug.Toggle));
        TDebug.LogWarning(string.Format("TDebugger.TDebug.LogWarning(),TDebug.Toggle = {0}", TDebug.Toggle));
        TDebug.LogError(string.Format("TDebugger.TDebug.LogError(),TDebug.Toggle = {0}", TDebug.Toggle));

        TDebug.Toggle = true;
        TDebug.Log(string.Format("TDebugger.TDebug.Log(),TDebug.Toggle = {0}", TDebug.Toggle));
        TDebug.Log($"sdfsdf{233333}");
        TDebug.LogWarning(string.Format("TDebugger.TDebug.Log(),TDebug.LogWarning = {0}", TDebug.Toggle));
        TDebug.LogError(string.Format("TDebugger.TDebug.LogError(),TDebug.Toggle = {0}", TDebug.Toggle));
	}
	
	// Update is called once per frame
	void Update () 
    {
	
	}
}
