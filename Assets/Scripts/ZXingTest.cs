using UnityEngine;
using ZXing;

public class ZXingTest : MonoBehaviour
{
    void Start()
    {
        Debug.Log("ZXing OK: " + typeof(BarcodeReader).FullName);
    }
}
