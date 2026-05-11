using UnityEngine;
using QFSW.QC;

public class DebugCommands : MonoBehaviour
{
  [Command("spawn_cube")]
    public static void SpawnDebugCube()
    {
       var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
       cube.transform.position =  UnityEngine.Random.insideUnitSphere * 3;
    }

}

