using System.Collections;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine;

public class GlobalVolumeManager : MonoBehaviour
{
    private ColorAdjustments colorAdjustment;
    private DepthOfField depthOfField;
    private bool initialized;
    private bool initializationWarningLogged;
    private float defaultSaturation;
    private float defaultPostExposure;
    private bool defaultDepthOfFieldActive;

    private void Awake()
    {
        EnsureInitialized();
    }

    /*
    public IEnumerator transitionExposureNegre(float duration)
    {
        float currentTemps = 0.0f;
        
        while (true)
        {
            currentTemps += Time.deltaTime;
            if (currentTemps < (duration / 2.0f))
            { //fem menys exposicio
                ColorAdjustment.postExposure.value = (currentTemps / (duration / 2.0f)) * (-8.0f);
                yield return null;
            }
            else
            { //tornem la expo a com estava
                ColorAdjustment.postExposure.value = (1.0f-((currentTemps - (duration / 2.0f)) / (duration / 2.0f))) * (-8.0f);
                yield return null; 
            }



            if (currentTemps > duration)
            {
                ColorAdjustment.postExposure.value = 0f; break;
            }
        }

        yield return null; 
    }
    */


    public IEnumerator transitionExposureBlanc(float duration)
    {
        if (!TryGetColorAdjustments(out ColorAdjustments adjustments))
        {
            yield break;
        }

        float currentTemps = 0.0f;

        while (true)
        {
            currentTemps += Time.deltaTime;
            if (currentTemps < (duration / 2.0f))
            { //fem menys exposicio
                adjustments.postExposure.value = defaultPostExposure + ((currentTemps / (duration / 2.0f)) * 8.0f);
                yield return null;
            }
            else
            { //tornem la expo a com estava
                adjustments.postExposure.value = defaultPostExposure + ((1.0f - ((currentTemps - (duration / 2.0f)) / (duration / 2.0f))) * 8.0f);
                yield return null;
            }



            if (currentTemps > duration)
            {
                adjustments.postExposure.value = defaultPostExposure;
                break;
            }
        }

        yield return null;
    }

    public void BlancINegre()
    {
        if (TryGetColorAdjustments(out ColorAdjustments adjustments))
        {
            adjustments.saturation.value = -100f;
        }
    }

    public void Color()
    {
        if (TryGetColorAdjustments(out ColorAdjustments adjustments))
        {
            adjustments.saturation.value = defaultSaturation;
        }
    }

    public void setBlur()
    {
        if (TryGetDepthOfField(out DepthOfField field))
        {
            field.active = true;
        }
    }

    public void clearBlur()
    {
        if (TryGetDepthOfField(out DepthOfField field))
        {
            field.active = defaultDepthOfFieldActive;
        }
    }

    private bool EnsureInitialized()
    {
        if (initialized)
        {
            return true;
        }

        Volume volume = GetComponent<Volume>();
        if (volume == null || volume.sharedProfile == null)
        {
            LogInitializationWarning("GlobalVolumeManager requires a Volume with a shared profile.");
            return false;
        }

        volume.sharedProfile.TryGet(out colorAdjustment);
        volume.sharedProfile.TryGet(out depthOfField);

        if (colorAdjustment == null)
        {
            LogInitializationWarning("GlobalVolumeManager could not find a ColorAdjustments override in the bound volume profile.");
        }
        else
        {
            defaultSaturation = colorAdjustment.saturation.value;
            defaultPostExposure = colorAdjustment.postExposure.value;
        }

        if (depthOfField == null)
        {
            LogInitializationWarning("GlobalVolumeManager could not find a DepthOfField override in the bound volume profile.");
        }
        else
        {
            defaultDepthOfFieldActive = depthOfField.active;
        }

        initialized = true;
        return true;
    }

    private bool TryGetColorAdjustments(out ColorAdjustments adjustments)
    {
        adjustments = null;
        if (!EnsureInitialized() || colorAdjustment == null)
        {
            return false;
        }

        adjustments = colorAdjustment;
        return true;
    }

    private bool TryGetDepthOfField(out DepthOfField field)
    {
        field = null;
        if (!EnsureInitialized() || depthOfField == null)
        {
            return false;
        }

        field = depthOfField;
        return true;
    }

    private void LogInitializationWarning(string message)
    {
        if (initializationWarningLogged)
        {
            return;
        }

        initializationWarningLogged = true;
        Debug.LogWarning(message, this);
    }
}
