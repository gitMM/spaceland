using UnityEngine;
using UnityEngine.InputSystem;

namespace DeathStar
{
    // First person in magnetic boots, for the station's hull outside, where there is no up (inside, StationWalker walks
    // upright in the station's gravity; the interiors were walked in these boots before they had a fixed up). The boots hold the walker to
    // whatever is under its feet and its up follows that floor: it walks up onto a wall it walks into, over convex edges
    // (into a hatch or socket's lining, round a tube's corners), and with the boots released (Space) it pushes off and
    // drifts straight where it looks, curled up, through a hatch or across a room, until it touches a surface, which
    // becomes its floor. It collides with the colliders of its own scene's physics (StationInteriors gives the rooms and
    // their window glass colliders). The transform is the feet, its up the walker's up and its forward the facing; the
    // eye camera follows a smoothed frame, so stepping onto a wall swings the view round instead of snapping it.
    public sealed class MagBootWalker : MonoBehaviour
    {
        public Camera eye;
        public float radius=0.3f,height=1.8f,eyeHeight=1.65f;
        public float walkSpeed=1.5f,runSpeed=3.2f,pushSpeed=2.5f,lookSpeed=0.1f;
        // Everything but Ignore Raycast, the walker's own layer.
        public LayerMask mask=~(1<<2);

        public bool Floating {get;private set;}
        public Vector3 Velocity=>velocity;
        public Vector3 EyePosition=>transform.position+transform.up*eyeHeight;
        public Quaternion EyeRotation=>transform.rotation*Quaternion.Euler(pitch,0,0);
        // What the last step did: "walk", "climb" (onto a wall), "wrap" (over an edge), "drift" or "land".
        public string Last {get;private set;}="";

        const float Skin=0.02f,FloatRadius=0.3f,Snap=0.35f,Reach=0.7f,Lip=0.06f;
        static readonly float[] Probes={0.3f,0.6f,0.9f,1.2f};
        // A surface ahead is a wall to climb when its normal is this far from up (cosine below), and not a ceiling.
        const float WallCos=0.5f,CeilingCos=-0.7f;
        const QueryTriggerInteraction Triggers=QueryTriggerInteraction.Ignore;
        Vector3 velocity;float pitch;
        CapsuleCollider body;
        readonly Collider[] overlaps=new Collider[16];
        // The camera: a frame that eases after the body's turns, and an offset that eases out the body's jumps.
        Quaternion frame=Quaternion.identity;Vector3 offset;

        PhysicsScene World=>gameObject.scene.GetPhysicsScene();

        void Awake()=>Ensure();
        void Ensure()
        {
            if(body!=null)return;
            body=GetComponent<CapsuleCollider>();if(!body)body=gameObject.AddComponent<CapsuleCollider>();
            body.isTrigger=true;body.radius=radius;body.height=height-Lip;body.center=new Vector3(0,(height+Lip)*0.5f,0);body.direction=1;
            gameObject.layer=2;
        }

        // Stands at feet with up, facing forward, boots on, looking level.
        public void Place(Vector3 feet,Vector3 up,Vector3 forward)
        {
            Ensure();
            transform.SetPositionAndRotation(feet,Quaternion.LookRotation(Vector3.ProjectOnPlane(forward,up),up));
            pitch=0;velocity=Vector3.zero;Floating=false;frame=transform.rotation;offset=Vector3.zero;Last="";
        }
        // Turns the head (and body about its up) to look at a point.
        public void LookAt(Vector3 target)
        {
            var d=target-EyePosition;var flat=Vector3.ProjectOnPlane(d,transform.up);
            if(flat.sqrMagnitude>1e-6f)transform.rotation=Quaternion.LookRotation(flat,transform.up);
            pitch=Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(Vector3.Dot(d.normalized,transform.up),-1,1))*Mathf.Rad2Deg,-89,89);
            frame=transform.rotation;
        }

        void Update()
        {
            var kb=Keyboard.current;var mouse=Mouse.current;
            Vector2 move=default,look=default;bool push=false,run=false;
            if(kb!=null)
            {
                if(kb.wKey.isPressed)move.y+=1;if(kb.sKey.isPressed)move.y-=1;
                if(kb.dKey.isPressed)move.x+=1;if(kb.aKey.isPressed)move.x-=1;
                push=kb.spaceKey.wasPressedThisFrame;run=kb.leftShiftKey.isPressed;
            }
            if(mouse!=null&&Cursor.lockState==CursorLockMode.Locked)look=mouse.delta.ReadValue()*lookSpeed;
            Step(move,look,push,run,Mathf.Min(Time.deltaTime,0.05f));
        }
        void LateUpdate()
        {
            if(eye==null)return;
            Ease(Time.deltaTime);
            eye.transform.SetPositionAndRotation(ViewPosition,ViewRotation);
        }
        // The camera's eased pose (the lab renders from it too).
        public Vector3 ViewPosition=>transform.position+offset+frame*Vector3.up*eyeHeight;
        public Quaternion ViewRotation=>frame*Quaternion.Euler(pitch,0,0);
        public void Ease(float dt)
        {
            float k=1-Mathf.Exp(-dt*10);
            frame=Quaternion.Slerp(frame,transform.rotation,k);offset*=Mathf.Exp(-dt*8);
        }

        // One step: move across the floor (x right, y forward), look (yaw and pitch in degrees), push off, run.
        public void Step(Vector2 move,Vector2 look,bool push,bool run,float dt)
        {
            Ensure();
            var yaw=Quaternion.Euler(0,look.x,0);transform.rotation*=yaw;frame*=yaw;
            pitch=Mathf.Clamp(pitch-look.y,-89,89);
            if(!Floating&&push){Floating=true;velocity=EyeRotation*Vector3.forward*pushSpeed;}
            if(Floating){Drift(dt);return;}
            Walk((transform.right*move.x+transform.forward*move.y)*((run?runSpeed:walkSpeed)*dt),dt);
        }

        void Walk(Vector3 delta,float dt)
        {
            Last="walk";
            var up=transform.up;
            // Walking into a wall (seen from shin to chest, over sills and lips, the nearest): it becomes the floor. Where
            // the floor runs into a leaning wall the foot of it stops the walker short, so held up with a wall within
            // reach, it steps on too.
            if(delta.sqrMagnitude<1e-10f){Ground(delta,dt);Depenetrate();return;}
            var dir=delta.normalized;var before=transform.position;
            bool ahead=false;RaycastHit wall=default;
            foreach(float at in Probes)
                if(World.Raycast(transform.position+up*at,dir,out var hit,radius+Reach,mask,Triggers)&&(!ahead||hit.distance<wall.distance)){wall=hit;ahead=true;}
            float c=ahead?Vector3.Dot(wall.normal,up):1;bool climbable=ahead&&c<WallCos&&c>CeilingCos;
            if(climbable&&wall.distance<radius+delta.magnitude+0.1f){Climb(wall.normal);return;}
            Slide(delta);
            if(climbable&&Vector3.Dot(transform.position-before,dir)<delta.magnitude*0.3f){Climb(wall.normal);return;}
            Ground(delta,dt);
            Depenetrate();
        }

        void Slide(Vector3 delta)
        {
            var up=transform.up;
            for(int i=0;i<3&&delta.sqrMagnitude>1e-10f;i++)
            {
                float len=delta.magnitude;var dir=delta/len;var p=transform.position;
                if(World.CapsuleCast(p+up*(radius+Lip),p+up*(height-radius),radius-Skin,dir,out var hit,len+Skin,mask,Triggers))
                {
                    float go=Mathf.Max(0,hit.distance-Skin);transform.position+=dir*go;
                    delta=Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(dir*(len-go),hit.normal),up);
                }
                else{transform.position+=delta;break;}
            }
        }

        // Keeps the feet on the floor and up along its normal; over a convex edge, wraps round it onto the face below;
        // with nothing underfoot at all, drifts on.
        void Ground(Vector3 delta,float dt)
        {
            var up=transform.up;var p=transform.position;
            if(World.Raycast(p+up*0.5f,-up,out var g,0.5f+Snap,mask,Triggers))
            {
                transform.position=g.point;
                if(Vector3.Dot(g.normal,up)>0.6f)transform.rotation=Quaternion.FromToRotation(up,g.normal)*transform.rotation;
                return;
            }
            var along=delta.sqrMagnitude>1e-10f?delta.normalized:transform.forward;
            if(World.Raycast(p-up*0.25f+along*0.15f,-along,out var e,0.8f,mask,Triggers)&&Vector3.Dot(e.normal,along)>0.5f)
            {
                var before=transform.position;
                transform.SetPositionAndRotation(e.point,Quaternion.LookRotation(Vector3.ProjectOnPlane(-up,e.normal),e.normal));
                offset+=before-transform.position;Last="wrap";
                return;
            }
            Floating=true;velocity=dt>0?delta/dt:Vector3.zero;Last="drift";
        }

        // Onto a wall ahead: the feet go to its foot, a little way up it, and its normal becomes up, walking on up it.
        void Climb(Vector3 normal)
        {
            var up=transform.up;var p=transform.position;var before=p;
            if(World.Raycast(p+up*0.1f,-normal,out var h,3f,mask,Triggers))p=h.point+up*(radius+Skin);
            transform.SetPositionAndRotation(p,Quaternion.LookRotation(Vector3.ProjectOnPlane(up,normal),normal));
            offset+=before-p;Last="climb";
            Depenetrate();
        }

        // Drifting, curled up round the eye: straight on until it touches something.
        void Drift(float dt)
        {
            Last="drift";
            var d=velocity*dt;float len=d.magnitude;if(len<1e-7f)return;var dir=d/len;
            if(World.SphereCast(EyePosition,FloatRadius,dir,out var hit,len+Skin,mask,Triggers))
            {
                transform.position+=dir*Mathf.Max(0,hit.distance-Skin);
                Land(hit.point,hit.normal);
            }
            else transform.position+=d;
        }

        // Touching a surface: it becomes the floor, feet where the walker touched it, still looking the same way.
        void Land(Vector3 point,Vector3 normal)
        {
            var before=EyePosition;var look=EyeRotation*Vector3.forward;
            var forward=Vector3.ProjectOnPlane(look,normal);
            if(forward.sqrMagnitude<1e-4f)forward=Vector3.ProjectOnPlane(transform.up,normal);
            if(forward.sqrMagnitude<1e-4f)forward=Vector3.ProjectOnPlane(transform.forward,normal);
            transform.SetPositionAndRotation(point,Quaternion.LookRotation(forward,normal));
            pitch=Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(Vector3.Dot(look,normal),-1,1))*Mathf.Rad2Deg,-89,89);
            Floating=false;velocity=Vector3.zero;Last="land";
            Depenetrate();
            offset+=before-EyePosition;
        }

        // Out of anything the body overlaps, leaving out its lowest Lip, so the boots stand on lips and lamps rather
        // than being pushed off them sideways.
        void Depenetrate()
        {
            float h=height-Lip;body.height=h;body.center=new Vector3(0,Lip+h*0.5f,0);
            for(int pass=0;pass<3;pass++)
            {
                var up=transform.up;var p=transform.position;bool moved=false;
                int n=World.OverlapCapsule(p+up*(Lip+radius),p+up*(height-radius),radius,overlaps,mask,Triggers);
                for(int i=0;i<n;i++)
                {
                    var c=overlaps[i];if(c==body)continue;
                    if(Physics.ComputePenetration(body,transform.position,transform.rotation,c,c.transform.position,c.transform.rotation,out var dir,out float dist)&&dist>0.001f)
                    {transform.position+=dir*(dist+0.001f);moved=true;}
                }
                if(!moved)break;
            }
        }
    }
}
