using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class LaserGunShooting : MonoBehaviour
{
    public Transform muzzlePoint;
    public InputActionReference shootAction;

    public float shootDistance = 100f;
    public float laserDuration = 0.1f;
    public LineRenderer laserLine;
    private Coroutine laserCoroutine;
    void OnEnable()
    {
        if (shootAction != null)
        {
            shootAction.action.Enable();
        }
    }

    void OnDisable()
    {
        if (shootAction != null)
        {
            shootAction.action.Disable();
        }
        if (laserCoroutine != null)
        {
            StopCoroutine(laserCoroutine);
            laserCoroutine = null;
        }

        if (laserLine != null)
        {
            laserLine.enabled = false;
        }
    }

    void Update()
    {
        // Check if the trigger was pressed this frame.
        if (shootAction == null || muzzlePoint == null)
        {
            return;
        }

        if (shootAction.action.WasPressedThisFrame())
        {
            Shoot();
        }
    }

    void Shoot()
    {
        RaycastHit hit;

        // By default,(if the laser doesn't hit anything) it extends forward to its maximum range.
        Vector3 endPoint = muzzlePoint.position +
                           muzzlePoint.forward * shootDistance;

        // If the ray hits an object, stop the laser at the hit point.
        if (Physics.Raycast(muzzlePoint.position,
                            muzzlePoint.forward,
                            out hit,
                            shootDistance))
        {
            endPoint = hit.point;
            Debug.Log("Laser hit: " + hit.collider.gameObject.name);
        }
        else
        {
            Debug.Log("Laser missed!");
        }

        // Show the laser between the muzzle and the endpoint.
        if (laserLine != null)
        {
            laserLine.SetPosition(0, muzzlePoint.position);
            laserLine.SetPosition(1, endPoint);
            laserLine.enabled = true;

            // Restart the visibility timer for each shot.
            if (laserCoroutine != null)
            {
                StopCoroutine(laserCoroutine);
            }

            laserCoroutine = StartCoroutine(HideLaser());
        }
    }

    IEnumerator HideLaser()
    {
        yield return new WaitForSeconds(laserDuration);

        laserLine.enabled = false;
        laserCoroutine = null;
    }
}