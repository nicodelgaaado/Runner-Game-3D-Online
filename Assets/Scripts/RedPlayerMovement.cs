using System.Collections;
using UnityEngine;
using PathCreation;
using UnityEngine.InputSystem;

public class RedPlayerMovement : MonoBehaviour
{
    private const float RigidbodyDrag = 3f;
    private const float RigidbodyAngularDrag = 10f;
    private const string RedCollisionLayerName = "RedCollision";
    private const string BlueCollisionLayerName = "BlueCollision";

    public float speed;
    private Animator animator;
    private Rigidbody myRigidbody;
    public float FallingThreshold = -10f;
    private Transform myTransform;

    public GlobalVolumeManager volumeManager;
    public canvasManager canvasManager;

    public BluePlayerMovement bluePlayer;
    private ObstacleManager obstacleManager;
    public AudioSource Winsound;
    public AudioSource deathSound;
    public AudioSource MagicTecles;
    public AudioSource runningsound;

    public PathCreator pathCreator1;
    public PathCreator pathCreator2;
    public PathCreator pathCreator3;
    public PathCreator pathCreator4;
    public PathCreator pathCreator5;

    //Nivell 2
    public float forceGrupPunxes1;
    public float forceGrupPunxes2;
    public float forceGrupPunxes3;
    public float forceGirador1;
    public float forceGirador2;

    //Nivell 1
    public float forceTorusNivell1;
    public float forced;


    //Nivell 3
    public float forcePendulum;
    public float forceAspa;


    //Nivell 5
    public float forcePorta;
    public float forceRoda;

    //Nivell 4
    public float forceMartell;
    public float forceBoxe;


    [HideInInspector]
    public int currentNivell;
    private bool Falling;
    float pathState;
    bool colisionat;
    bool fentRestart;
    bool canviantNivell;
    bool guanyatRed;
    bool perdutRed;
    bool moveForwardRequested;
    bool waitForMoveKeyRelease;


    void Start()
    {
        if (RunnerGame.Online.OnlineSceneRuntime.DisableLegacyRuntimeComponentIfBlocked(this, deactivateGameObject: true))
        {
            return;
        }

        animator = GetComponent<Animator>();
        myRigidbody = GetComponent<Rigidbody>();
        myTransform = transform;
        CacheObstacleManager();
        ConfigureRigidbody();
        ConfigurePlayerCollisionLayers();
        ResetMoveInputGate();
        canviantNivell = false;
        guanyatRed = false;
        perdutRed = false;
        animator.SetBool("isFalling", false);
        animator.SetBool("RedCry", false);
        animator.SetBool("Climb", false);
        animator.SetBool("RedWin", false);
        ComensaNivell(1);
    }

    private void Update()
    {
        moveForwardRequested = false;

        if (canviantNivell || guanyatRed || perdutRed)
        {
            runningsound.Pause();
            return;
        }

        if (TryHandleLevelChange())
        {
            return;
        }

        Falling = myRigidbody.linearVelocity.y < FallingThreshold;
        if (Falling)
        {
            runningsound.Pause();
            animator.SetBool("isMoving", false);
        }

        if (colisionat)
        {
            if (!fentRestart)
            {
                runningsound.Pause();
                animator.SetBool("isFalling", true);
                fentRestart = true;
                StartCoroutine(restartNivell());
            }
            return;
        }

        if (Falling || fentRestart)
        {
            return;
        }

        if (waitForMoveKeyRelease)
        {
            if (!IsMoveKeyPressed())
            {
                waitForMoveKeyRelease = false;
            }

            runningsound.Pause();
            animator.SetBool("isMoving", false);
            return;
        }

        moveForwardRequested = IsMoveKeyPressed();
        if (moveForwardRequested)
        {
            if (!runningsound.isPlaying)
            {
                runningsound.Play();
            }
        }
        else
        {
            runningsound.Pause();
            animator.SetBool("isMoving", false);
        }
    }

    private void FixedUpdate()
    {
        if (!moveForwardRequested || Falling || colisionat || fentRestart || canviantNivell || guanyatRed || perdutRed)
        {
            return;
        }

        if (currentNivell == 1)
        {
            MouNivell1();
        }
        else if (currentNivell == 2)
        {
            MouNivell2();
        }
        else if (currentNivell == 3)
        {
            MouNivell3();
        }
        else if (currentNivell == 4)
        {
            MouNivell4();
        }
        else if (currentNivell == 5)
        {
            MouNivell5();
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        string collisionName = collision.collider.name;
        Vector3 collisionNormal = GetCollisionNormal(collision);

        if (collisionName == "punxes1" || collisionName == "punxes2" || collisionName == "punxes3" || collisionName == "punxes4")
        {
            ApplyHit(new Vector3(-1f, 1f, -1f) * forceGrupPunxes1);
        }
        else if (collisionName == "punxes8")
        {
            ApplyHit(new Vector3(0f, 1f, 1f) * forceGrupPunxes2);
        }
        else if (collisionName == "punxes11")
        {
            ApplyHit(new Vector3(-1f, 1f, 0f) * forceGrupPunxes3);
        }
        else if (collisionName == "cGran1")
        {
            ApplyHit(collisionNormal * forceGirador1);
        }
        else if (collisionName == "cGran2")
        {
            ApplyHit(collisionNormal * forceGirador2);
        }
        else if (collisionName == "Torus1" || collisionName == "Torus2")
        {
            ApplyHit(collisionNormal * forceTorusNivell1);
        }
        else if (collisionName == "d2" || collisionName == "d1" || collisionName == "d3")
        {
            ApplyHit(collisionNormal * forced);
        }
        else if (collisionName == "f11" || collisionName == "f22" || collisionName == "f33" || collisionName == "f44" || collisionName == "f55" || collisionName == "f66" || collisionName == "f77")
        {
            ApplyHit(collisionNormal * forcePendulum);
        }
        else if (collisionName == "pal2aspa1" || collisionName == "pal1aspa1" || collisionName == "pal1aspa2" || collisionName == "pal2aspa2")
        {
            ApplyHit(collisionNormal * forceAspa);
        }
        else if (collisionName == "porta11" || collisionName == "porta12" || collisionName == "porta21" || collisionName == "porta22" || collisionName == "porta31" || collisionName == "porta32" || collisionName == "porta41" || collisionName == "porta42")
        {
            ApplyHit(collisionNormal * forcePorta);
        }
        else if (collisionName == "Roda1Objecte1" || collisionName == "Roda1Objecte2" || collisionName == "Roda1Objecte3" || collisionName == "Roda1Objecte4" || collisionName == "Roda2Objecte1" || collisionName == "Roda2Objecte2" || collisionName == "Roda2Objecte3" || collisionName == "Roda2Objecte4")
        {
            ApplyHit(collisionNormal * forceRoda);
        }
        else if (collisionName == "d8")
        {
            ApplyHit(collisionNormal * forced);
        }
        else if (collisionName == "cGran3")
        {
            ApplyHit(collisionNormal * forceGirador1);
        }
        else if (collisionName == "picMartell1" || collisionName == "picMartell2" || collisionName == "picMartell3" || collisionName == "picMartell4" || collisionName == "picMartell5" || collisionName == "picMartell6" || collisionName == "picMartell7" || collisionName == "picMartell8")
        {
            ApplyHit(collisionNormal * forceMartell);
        }
        else if (collisionName == "pal2aspa3" || collisionName == "pal1aspa3" || collisionName == "pal2aspa4" || collisionName == "pal1aspa4")
        {
            ApplyHit(collisionNormal * forceAspa);
        }
        else if (collisionName == "Torus3")
        {
            ApplyHit(collisionNormal * forceTorusNivell1);
        }
        else if (collisionName == "picoBoxa1" || collisionName == "picoBoxa2" || collisionName == "picoBoxa3" || collisionName == "picoBoxa4")
        {
            ApplyHit(collisionNormal * forceBoxe, true);
        }
    }

    private bool TryHandleLevelChange()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return false;
        }

        if (keyboard.digit1Key.wasPressedThisFrame && currentNivell != 1)
        {
            BeginLevelChange(1);
            return true;
        }

        if (keyboard.digit2Key.wasPressedThisFrame && currentNivell != 2)
        {
            BeginLevelChange(2);
            return true;
        }

        if (keyboard.digit3Key.wasPressedThisFrame && currentNivell != 3)
        {
            BeginLevelChange(3);
            return true;
        }

        if (keyboard.digit4Key.wasPressedThisFrame && currentNivell != 4)
        {
            BeginLevelChange(4);
            return true;
        }

        if (keyboard.digit5Key.wasPressedThisFrame && currentNivell != 5)
        {
            BeginLevelChange(5);
            return true;
        }

        return false;
    }

    private void BeginLevelChange(int nivell)
    {
        ResetMoveInputGate();
        runningsound.Pause();
        MagicTecles.Play();
        canviantNivell = true;
        animator.SetBool("isMoving", false);
        animator.SetBool("isFalling", false);
        StartCoroutine(canviaNivell(nivell));
    }

    private void ConfigureRigidbody()
    {
        myRigidbody.linearDamping = RigidbodyDrag;
        myRigidbody.angularDamping = RigidbodyAngularDrag;
        myRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        myRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        myRigidbody.constraints = RigidbodyConstraints.FreezeRotation;
    }

    private void ConfigurePlayerCollisionLayers()
    {
        int redCollisionLayer = LayerMask.NameToLayer(RedCollisionLayerName);
        int blueCollisionLayer = LayerMask.NameToLayer(BlueCollisionLayerName);

        if (redCollisionLayer >= 0 && blueCollisionLayer >= 0)
        {
            Physics.IgnoreLayerCollision(redCollisionLayer, blueCollisionLayer, true);
        }
    }

    private void ClearPhysicsMotion()
    {
        myRigidbody.linearVelocity = Vector3.zero;
        myRigidbody.angularVelocity = Vector3.zero;
    }

    private bool IsMoveKeyPressed()
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.wKey.isPressed;
    }

    private void ResetMoveInputGate()
    {
        moveForwardRequested = false;
        waitForMoveKeyRelease = true;
    }

    private Vector3 GetCollisionNormal(Collision collision)
    {
        if (collision.contactCount > 0)
        {
            return collision.GetContact(0).normal;
        }

        return -myTransform.forward;
    }

    private void ApplyHit(Vector3 force, bool enableGravity = false)
    {
        if (colisionat)
        {
            return;
        }

        moveForwardRequested = false;
        myRigidbody.angularVelocity = Vector3.zero;
        if (enableGravity)
        {
            myRigidbody.useGravity = true;
        }

        myRigidbody.AddForce(force);
        colisionat = true;
        deathSound.Play();
        animator.SetBool("isMoving", false);
    }

    private void ComensaNivell(int nivell)
    {
        CacheObstacleManager();
        ConfigureRigidbody();
        myRigidbody.useGravity = true;
        ClearPhysicsMotion();
        ResetMoveInputGate();
        Falling = false;
        colisionat = false;
        currentNivell = nivell;
        if (obstacleManager != null)
        {
            obstacleManager.SetActiveLevel(nivell);
        }
        if (canvasManager != null)
        {
            canvasManager.activaCanviaLevelText(currentNivell);
        }
        pathState = 0f;
        animator.SetBool("isMoving", false);
        animator.SetBool("Climb", false);

        if (nivell == 1)
        {
            TeleportTo(pathCreator1.path.GetPointAtDistance(pathState), Quaternion.LookRotation(Vector3.right, Vector3.up));
        }
        else if (nivell == 2)
        {
            TeleportTo(pathCreator2.path.GetPointAtDistance(pathState), Quaternion.LookRotation(Vector3.forward, Vector3.up));
        }
        else if (nivell == 3)
        {
            TeleportTo(pathCreator3.path.GetPointAtDistance(pathState), Quaternion.LookRotation(Vector3.forward, Vector3.up));
        }
        else if (nivell == 4)
        {
            TeleportTo(pathCreator4.path.GetPointAtDistance(pathState), Quaternion.LookRotation(Vector3.back, Vector3.up));
        }
        else if (nivell == 5)
        {
            TeleportTo(pathCreator5.path.GetPointAtDistance(pathState), Quaternion.LookRotation(Vector3.right, Vector3.up));
        }
    }

    private void TeleportTo(Vector3 position, Quaternion rotation)
    {
        myRigidbody.position = position;
        myRigidbody.rotation = rotation;
    }

    private void CacheObstacleManager()
    {
        if (obstacleManager == null)
        {
            obstacleManager = FindAnyObjectByType<ObstacleManager>();
        }
    }

    private void MoveAlongPath(PathCreator pathCreator, float finishDistance)
    {
        pathState += speed * Time.fixedDeltaTime;
        if (pathState > finishDistance)
        {
            StartCoroutine(guanyat());
            return;
        }

        Vector3 currentPosition = myRigidbody.position;
        Vector3 pathPosition = pathCreator.path.GetPointAtDistance(pathState);
        Vector3 pathPositionNext = pathCreator.path.GetPointAtDistance(pathState * 1.01f);
        Vector3 targetPosition = new Vector3(pathPosition.x, currentPosition.y, pathPosition.z);
        Vector3 targetForward = new Vector3(pathPositionNext.x, currentPosition.y, pathPositionNext.z) - targetPosition;

        myRigidbody.useGravity = true;
        myRigidbody.MovePosition(targetPosition);
        if (targetForward.sqrMagnitude > 0.0001f)
        {
            myRigidbody.MoveRotation(Quaternion.LookRotation(targetForward.normalized, Vector3.up));
        }

        animator.SetBool("isMoving", true);
    }

    private void MouNivell1()
    {
        MoveAlongPath(pathCreator1, 480f);
    }

    private void MouNivell2()
    {
        MoveAlongPath(pathCreator2, 309f);
    }

    private void MouNivell3()
    {
        MoveAlongPath(pathCreator3, 372f);
    }

    private void MouNivell4()
    {
        pathState += speed * Time.fixedDeltaTime;
        if (pathState > 387f)
        {
            StartCoroutine(guanyat());
            return;
        }

        Vector3 pathPosition = pathCreator4.path.GetPointAtDistance(pathState);
        if (pathState > 286.0f)
        {
            myRigidbody.useGravity = false;
            myRigidbody.MovePosition(pathPosition);
            animator.SetBool("Climb", true);
        }
        else
        {
            Vector3 currentPosition = myRigidbody.position;
            Vector3 pathPositionNext = pathCreator4.path.GetPointAtDistance(pathState * 1.01f);
            Vector3 targetPosition = new Vector3(pathPosition.x, currentPosition.y, pathPosition.z);
            Vector3 targetForward = new Vector3(pathPositionNext.x, currentPosition.y, pathPositionNext.z) - targetPosition;

            myRigidbody.useGravity = true;
            myRigidbody.MovePosition(targetPosition);
            if (targetForward.sqrMagnitude > 0.0001f)
            {
                myRigidbody.MoveRotation(Quaternion.LookRotation(targetForward.normalized, Vector3.up));
            }

            animator.SetBool("isMoving", true);
            animator.SetBool("Climb", false);
        }
    }

    private void MouNivell5()
    {
        MoveAlongPath(pathCreator5, 632f);
    }

    private IEnumerator restartNivell()
    {
        animator.SetBool("Climb", false);
        ResetMoveInputGate();
        yield return new WaitForSeconds(2f);
        if (canvasManager != null)
        {
            StartCoroutine(canvasManager.transitionRedExposureNegre(2f));
        }
        animator.SetBool("isFalling", false);
        yield return new WaitForSeconds(1f);
        ClearPhysicsMotion();
        ComensaNivell(currentNivell);
        ResetMoveInputGate();
        yield return new WaitForSeconds(1f);
        fentRestart = false;
    }

    private IEnumerator canviaNivell(int nivell)
    {
        animator.SetBool("Climb", false);
        ResetMoveInputGate();
        StartCoroutine(volumeManager.transitionExposureBlanc(2f));
        yield return new WaitForSeconds(1f);
        ClearPhysicsMotion();
        animator.SetBool("RedWin", false);
        animator.SetBool("RedCry", false);
        ComensaNivell(nivell);
        ResetMoveInputGate();
        yield return new WaitForSeconds(1f);
        canviantNivell = false;
        guanyatRed = false;
        perdutRed = false;
    }

    private IEnumerator guanyat()
    {
        Winsound.Play();
        StartCoroutine(bluePlayer.perdut());
        guanyatRed = true;
        canviantNivell = true;
        ResetMoveInputGate();
        animator.SetBool("RedWin", true);
        if (currentNivell != 5)
        {
            yield return new WaitForSeconds(3f);
            StartCoroutine(canviaNivell(currentNivell + 1));
        }
        else
        {
            if (canvasManager != null)
            {
                canvasManager.guanyaCredits();
            }
        }
    }

    public IEnumerator perdut()
    {
        perdutRed = true;
        canviantNivell = true;
        ResetMoveInputGate();
        animator.SetBool("RedCry", true);
        if (currentNivell != 5)
        {
            yield return new WaitForSeconds(3f);
            StartCoroutine(canviaNivell(currentNivell + 1));
        }
    }

    public void ComensaPrincipi()
    {
        ComensaNivell(1);
    }
}
