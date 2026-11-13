using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Generates the enemy's procedural humanoid clips (Run, Search) and its Animator Controller.
/// Menu: Tools > Enemy > Build Animations. Re-run after tweaking the numbers below.
///
/// Clips are written as humanoid muscle curves, so they work on any humanoid avatar and can be
/// swapped for mocap clips (e.g. Mixamo) later by replacing the Motion on the controller states.
///
/// Muscle sign conventions (measured on this rig):
///   Upper Leg Front-Back: ~0.6 standing, lower = thigh forward, higher = thigh back.
///   Lower Leg Stretch:    1 = straight knee, lower = more bent.
///   Arm Down-Up:          -1 = arm hanging down.   Arm Front-Back: negative = forward.
///   Forearm Stretch:      1 = straight elbow.      Spine Front-Back: negative = lean forward.
///   Head/Neck Turn:       positive = turn right.
/// </summary>
public static class EnemyAnimationBuilder
{
    const string ModelPath = "Assets/white_chandrasekar/model.fbx";
    const string OutDir = "Assets/Animations/Enemy";
    const float Fps = 30f;

    [MenuItem("Tools/Enemy/Build Animations")]
    public static void BuildAll()
    {
        Directory.CreateDirectory(OutDir);
        var standing = GetStandingMuscles(out float bodyHeight);

        var run = BuildClip("Enemy_Run", 0.7f, standing, bodyHeight, RunPose);
        var search = BuildClip("Enemy_Search", 4.8f, standing, bodyHeight, SearchPose);
        BuildController(run, search);
        AssetDatabase.SaveAssets();
        Debug.Log("Enemy animations built in " + OutDir);
    }

    // ---------------------------------------------------------------- poses

    // p = normalised time within one run stride (0..1). One stride = left + right step.
    static void RunPose(float t, float length, Dictionary<string, float> m, ref float bob, ref float lean)
    {
        float p = t / length;
        Legs(m, p * 2f * Mathf.PI, run: true);
        Arms(m, p * 2f * Mathf.PI, down: -0.55f, swing: 0.6f, swingCentre: 0.25f, forearm: -0.15f, forearmSwing: 0.2f);

        m["Spine Front-Back"] = -0.3f;          // lean into the run
        m["Chest Front-Back"] = -0.1f;
        m["Neck Nod Down-Up"] = 0.1f;           // keep eyes forward despite the lean
        m["Head Nod Down-Up"] = 0.05f;
        m["Spine Twist Left-Right"] = -0.12f * Mathf.Sin(p * 2f * Mathf.PI);
        m["Chest Twist Left-Right"] = -0.08f * Mathf.Sin(p * 2f * Mathf.PI);
        Fists(m, -0.55f);

        // lowest at each mid-stance (twice per stride), highest in the flight phase
        bob = -0.04f - 0.035f * Mathf.Cos(p * 4f * Mathf.PI);
        lean = 10f;
    }

    // Search: slow cautious walk (4 strides of 1.2 s) while scanning left and right.
    static void SearchPose(float t, float length, Dictionary<string, float> m, ref float bob, ref float lean)
    {
        const float stride = 1.2f;
        float phi = (t / stride) * 2f * Mathf.PI;
        float look = (t / length) * 2f * Mathf.PI;   // one full left-right scan per clip

        Legs(m, phi, run: false);
        Arms(m, phi, down: -0.75f, swing: 0.22f, swingCentre: 0.35f, forearm: 0.55f, forearmSwing: 0.1f);

        float scan = Mathf.Sin(look);
        m["Head Turn Left-Right"] = 0.65f * scan;
        m["Neck Turn Left-Right"] = 0.45f * scan;
        m["Chest Twist Left-Right"] = 0.12f * scan;
        m["Spine Twist Left-Right"] = 0.06f * scan - 0.04f * Mathf.Sin(phi);
        // glance down a little at the turn-arounds, as if checking the floor
        m["Head Nod Down-Up"] = -0.15f * Mathf.Abs(scan) * Mathf.Abs(scan);
        m["Spine Front-Back"] = -0.08f;
        Fists(m, -0.15f);

        // walking: highest at mid-stance (inverted pendulum)
        bob = 0.012f * Mathf.Cos(phi * 2f) - 0.01f;
        lean = 2f;
    }

    static void Legs(Dictionary<string, float> m, float phi, bool run)
    {
        for (int side = 0; side < 2; side++)
        {
            string s = side == 0 ? "Left" : "Right";
            float f = phi + side * Mathf.PI;
            float sin = Mathf.Sin(f), cos = Mathf.Cos(f);

            // thigh forward peak at f = pi/2, back peak at 3pi/2
            float thigh, knee, foot;
            if (run)
            {
                thigh = 0.275f - 0.725f * sin;
                // big heel kick while the leg swings through (centred on f = 0),
                // slight absorb at mid-stance (f = pi)
                knee = 0.8f - 1.35f * Mathf.Pow(Mathf.Max(0f, cos), 1.5f) - 0.15f * Mathf.Max(0f, -cos);
                foot = 0.25f * sin;
            }
            else
            {
                thigh = 0.47f - 0.47f * sin;
                knee = 0.97f - 0.7f * Mathf.Pow(Mathf.Max(0f, cos), 2f);
                foot = 0.15f * sin;
            }
            // neutral twist so the shin folds straight back instead of flaring outwards
            m[s + " Upper Leg Twist In-Out"] = 0f;
            m[s + " Lower Leg Twist In-Out"] = 0f;
            m[s + " Upper Leg In-Out"] = 0.05f;
            m[s + " Upper Leg Front-Back"] = thigh;
            m[s + " Lower Leg Stretch"] = knee;
            m[s + " Foot Up-Down"] = foot;
        }
    }

    static void Arms(Dictionary<string, float> m, float phi, float down, float swing, float swingCentre,
                     float forearm, float forearmSwing)
    {
        for (int side = 0; side < 2; side++)
        {
            string s = side == 0 ? "Left" : "Right";
            // each arm swings opposite its own leg: forward when that leg is back (f = 3pi/2)
            float sin = Mathf.Sin(phi + side * Mathf.PI);
            m[s + " Arm Down-Up"] = down;
            m[s + " Arm Front-Back"] = swingCentre + swing * sin;
            m[s + " Forearm Stretch"] = forearm + forearmSwing * sin;
        }
    }

    static void Fists(Dictionary<string, float> m, float curl)
    {
        foreach (var side in new[] { "Left", "Right" })
            foreach (var finger in new[] { "Index", "Middle", "Ring", "Little" })
                for (int j = 1; j <= 3; j++)
                    m[side + " " + finger + " " + j + " Stretched"] = curl;
    }

    // ---------------------------------------------------------------- clip writing

    delegate void PoseFn(float t, float length, Dictionary<string, float> muscles, ref float bob, ref float leanDeg);

    static AnimationClip BuildClip(string name, float length, float[] standing, float bodyHeight, PoseFn pose)
    {
        string[] names = HumanTrait.MuscleName;
        int frames = Mathf.RoundToInt(length * Fps);
        var curves = new Dictionary<string, AnimationCurve>();
        var rootY = new AnimationCurve();
        var rootQx = new AnimationCurve();
        var rootQw = new AnimationCurve();

        for (int f = 0; f <= frames; f++)
        {
            float t = f / Fps;
            var m = new Dictionary<string, float>();
            float bob = 0f, lean = 0f;
            pose(t % length, length, m, ref bob, ref lean);
            if (f == frames) { m.Clear(); bob = 0; lean = 0; pose(0f, length, m, ref bob, ref lean); } // exact loop

            for (int i = 0; i < names.Length; i++)
            {
                float v = m.TryGetValue(names[i], out var o) ? o : standing[i];
                string prop = ClipPropertyName(names[i]);
                if (!curves.TryGetValue(prop, out var c)) curves[prop] = c = new AnimationCurve();
                c.AddKey(t, v);
            }
            rootY.AddKey(t, bodyHeight + bob);
            var q = Quaternion.Euler(lean, 0f, 0f);
            rootQx.AddKey(t, q.x);
            rootQw.AddKey(t, q.w);
        }

        string path = OutDir + "/" + name + ".anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip { name = name };
            AssetDatabase.CreateAsset(clip, path);
        }
        clip.ClearCurves();
        clip.frameRate = Fps;

        void Set(string prop, AnimationCurve c) =>
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), prop), c);

        foreach (var kv in curves) Set(kv.Key, kv.Value);
        Set("RootT.x", AnimationCurve.Constant(0, length, 0));
        Set("RootT.y", rootY);
        Set("RootT.z", AnimationCurve.Constant(0, length, 0));
        Set("RootQ.x", rootQx);
        Set("RootQ.y", AnimationCurve.Constant(0, length, 0));
        Set("RootQ.z", AnimationCurve.Constant(0, length, 0));
        Set("RootQ.w", rootQw);

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        settings.loopBlend = false;
        settings.loopBlendOrientation = true;     // bake root rotation into pose (no root motion)
        settings.loopBlendPositionY = true;
        settings.loopBlendPositionXZ = true;
        settings.keepOriginalOrientation = true;
        settings.keepOriginalPositionY = true;
        settings.keepOriginalPositionXZ = true;
        settings.heightFromFeet = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    // HumanTrait names fingers "Left Index 1 Stretched"; clip bindings use "LeftHand.Index.1 Stretched".
    static string ClipPropertyName(string muscle)
    {
        foreach (var side in new[] { "Left", "Right" })
            foreach (var finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
            {
                string prefix = side + " " + finger + " ";
                if (muscle.StartsWith(prefix))
                {
                    string rest = muscle.Substring(prefix.Length);           // "1 Stretched" or "Spread"
                    if (rest == "Spread") return side + "Hand." + finger + ".Spread";
                    return side + "Hand." + finger + "." + rest.Substring(0, 1) + rest.Substring(1);
                }
            }
        return muscle;
    }

    // Muscle values of the model's bind (T-)pose, with arms lowered; used for every muscle a pose doesn't set.
    static float[] GetStandingMuscles(out float bodyHeight)
    {
        var avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().First();
        var go = (GameObject)UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
        try
        {
            using var handler = new HumanPoseHandler(avatar, go.transform);
            var pose = new HumanPose();
            handler.GetHumanPose(ref pose);
            bodyHeight = pose.bodyPosition.y;
            return pose.muscles;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    // ---------------------------------------------------------------- controller

    static void BuildController(AnimationClip run, AnimationClip search)
    {
        string path = OutDir + "/Enemy.controller";
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (ctrl == null)
            ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);

        foreach (var p in ctrl.parameters.ToArray()) ctrl.RemoveParameter(p);
        ctrl.AddParameter(new AnimatorControllerParameter { name = "IsRunning", type = AnimatorControllerParameterType.Bool, defaultBool = true });
        ctrl.AddParameter(new AnimatorControllerParameter { name = "AnimSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });

        var sm = ctrl.layers[0].stateMachine;
        foreach (var s in sm.states.ToArray()) sm.RemoveState(s.state);

        var runState = sm.AddState("Run", new Vector3(300, 0, 0));
        runState.motion = run;
        runState.speedParameter = "AnimSpeed";
        runState.speedParameterActive = true;

        var searchState = sm.AddState("Search", new Vector3(300, 120, 0));
        searchState.motion = search;
        searchState.speedParameter = "AnimSpeed";
        searchState.speedParameterActive = true;

        sm.defaultState = runState;

        var toSearch = runState.AddTransition(searchState);
        toSearch.hasExitTime = false;
        toSearch.duration = 0.25f;
        toSearch.AddCondition(AnimatorConditionMode.IfNot, 0, "IsRunning");

        var toRun = searchState.AddTransition(runState);
        toRun.hasExitTime = false;
        toRun.duration = 0.2f;
        toRun.AddCondition(AnimatorConditionMode.If, 0, "IsRunning");

        EditorUtility.SetDirty(ctrl);
    }
}
