using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ItemOutlineRaycast : MonoBehaviour
{
    public Camera cam;
    public float distance = 5f;

    private HoverOutline currentObj;

    void Update()
    {
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, distance))
        {
            HoverOutline ho = hit.collider.GetComponent<HoverOutline>();

            if (ho != null)
            {
                if (currentObj != ho)
                {
                    // ปิดของเก่า
                    if (currentObj) currentObj.SetOutline(false);

                    // เปิดของใหม่
                    currentObj = ho;
                    currentObj.SetOutline(true);
                }
                return;
            }
        }

        // ถ้าไม่เจอ object เลย
        if (currentObj)
        {
            currentObj.SetOutline(false);
            currentObj = null;
        }
    }
}