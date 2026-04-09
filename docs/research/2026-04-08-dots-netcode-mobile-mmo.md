
  Research Report

Report: Production-readiness and engineering guidance for Unity DOTS Entities + 
        Netcode for Entities for large-scale mobile MMOs (target: 2025)         

Executive summary                                                               

 • DOTS (Entities + C# Job System + Burst) is a data‑oriented toolset with      
   production adoption by multiple studios; the Entities package is supported on
   Unity 2022.3 LTS and later, and DOTS development continues on a public       
   roadmap. [1], [2], [10], [32]                                                
 • Netcode for Entities is actively maintained and has been used in prototypes  
   and real projects up to several hundred concurrent users in an off‑the‑shelf 
   load test; it includes authoritative‑server workflows and prediction         
   primitives but also shows real-world fragility (host‑migration, snapshot/ack 
   issues and intermittent runtime errors) that have been addressed             
   incrementally in changelogs. Expect active patching and to pin to specific   
   package/editor combinations. [3], [6], [5], [4], [8], [33], [9]              

The remainder of the report synthesizes the available evidence into actionable  
guidance across the requested dimensions.                                       

1. Production readiness and maturity                                            

Overview and support status                                                     

 • Entities (ECS) is part of Unity's DOTS and is shipped as a package; it       
   requires Unity 2022.3.0f1 or later and is intended for production use across 
   platforms; Unity’s DOTS roadmap remains active (consolidation, “ECS for All” 
   work). [1], [10], [32]                                                       
 • DOTS components (ECS, Job System, Burst) are used in multiple shipped or     
   in‑production titles (examples include Stunlock’s V Rising tooling, Ramen    
   VR’s Zenith, Electric Square and others), demonstrating real adoption in     
   large and multiplayer projects. [2], [33], [9]                               

Stability, known bugs, and maintenance cadence                                  

 • Netcode for Entities shows ongoing maintenance with detailed changelogs that 
   fix nontrivial issues (e.g., host‑migration problems, snapshot ack-window    
   bugs, physics interpolation jitter and buffer capacity improvements). Follow 
   changelogs for fixes that directly affect large‑scale replication behaviour. 
   [3], [6], [5], [4]                                                           
 • The package is actively evolved; Unity has shifted Netcode/Transport toward  
   being built‑in in newer editor lines (meaning package updates will align with
   editor updates), so plan editor/package version pinning and tracking of      
   engine releases. [32]                                                        

Real‑world tests and case evidence                                              

 • Public load tests demonstrate both strengths and limits: an off‑the‑shelf    
   server supported 700+ concurrent users at 30 updates/sec without CPU/GPU/RAM 
   bottlenecks on the test hardware, but visible stuttering appeared once       
   network traffic became the bottleneck at 1,000+ users; other community       
   reports place practical Netcode-for‑Entities usage around sub‑100 concurrent 
   players per scene or ~80 connections in some setups. These statements coexist
   in the record and must be treated as context‑dependent: successful           
   higher‑concurrency tests exist but are not universally reproducible without  
   careful architecture and tuning. [8], [12], [27]                             

Recommended engine/package strategy                                             

 • Use Unity 2022.3 LTS (or later LTS-compatible DOTS-supported editors) for    
   Entities stability; pin Entities and Netcode package versions to a known good
   set and track changelogs that reference server‑relevant fixes (host          
   migration, snapshot/ack handling, transport changes). The Entities manual and
   Netcode changelogs should be a required part of the project's                
   dependency‑upgrade process. [1], [3], [6], [32]                              

Evidence gaps (production-readiness)                                            

 • The available public evidence does not include a published, detailed         
   postmortem of a DOTS + Netcode for Entities deployment at 1k–10k concurrent  
   active players in production mobile MMO conditions (mobile clients over      
   cellular/Wi‑Fi, real region distribution, sustained daily operation). The    
   team should plan internal benchmarks and pilot deployments.                  

2. Server architecture patterns and trade-offs                                  

Authoritative dedicated servers and ownership/authority model                   

 • Netcode for Entities supports an authoritative server model and client‑side  
   prediction primitives intended for low‑latency gameplay. For MMOs that       
   require authoritative world simulation, this is the normative model in       
   Netcode for Entities. Use authoritative servers (server world) for           
   authoritative ticked simulation and treat clients as thin/predicted inputs.  
   [11]                                                                         

Interest management (AOI) and relevance filtering                               

 • Netcode includes built‑in distance‑based importance and chunk/tile           
   partitioning (GhostDistanceImportance, GhostImportance and                   
   GhostDistancePartitioningSystem) to selectively replicate ghosts to clients  
   based on tile/granularity and per‑chunk culling. These built‑ins compute     
   importance per chunk (not per entity), so plan chunking/granularity to match 
   game AOI needs. [41], [49]                                                   

Spatial sharding and entity partitioning                                        

 • Implement explicit spatial partitioning (voxel or spatial‑hash grids) to     
   reduce neighbor checks and limit which ghosts are considered for each client;
   community projects using DOTS (voxel grid / spatial hashing) illustrate      
   practical patterns for high entity counts. Use chunk‑level importance and    
   minimize frequent entity chunk moves (they are performance‑costly). [42],    
   [43], [49]                                                                   

Hybrid/sharded topologies (region servers + proxies)                            

 • Standard cloud + container orchestration patterns (Agones + Kubernetes)      
   support dedicated game servers (region or zone partitions). Integration      
   examples and community tooling (Netcode+Agones repository materials) show    
   feasible op patterns for dedicated server fleets and allocation. Consider    
   region/zone servers that own authoritative state for a world shard and use   
   edge proxies or session/replica frontends for connection management,         
   matchmaking and client NAT traversal. [19], [24], [35], [44]                 

Load balancing, autoscaling and cross‑shard communication                       

 • Agones and Kubernetes toolkits (and autoscaler patterns) are recommended for 
   server fleet orchestration and predictable auto‑scaling; Agones supports     
   allocation and capacity policies and can be integrated into a typical        
   matchmaking → allocation flow. For cross‑shard communication (e.g., entity   
   handoffs between adjacent area servers) design explicit, minimal handoff     
   messages, and limit cross‑server replication to authoritative ownership      
   transfer. [24], [44], [19], [35]                                             

Ownership recommendations for thousands of entities                             

 • Keep authority coarse (zone/region or partition) rather than per‑entity where
   possible. Use server worlds to own authoritative sim state, and use client   
   predictions only for local player‑controlled entities. Combine per‑chunk     
   importance and AOI to limit replication; avoid full‑world replication to     
   mobile clients. [11], [41], [49]                                             

3. Networking and synchronization strategies                                    

Ticking, server updates and determinism                                         

 • Netcode for Entities separates client and server worlds and runs the server  
   world on a fixed timestep (ClientServerTickRate). The server can do multiple 
   catch‑up steps if a frame lags. Plan server tick configuration carefully: use
   server‑fixed ticks for authority and reconcile to clients using snapshot     
   history and interpolation windows. [47]                                      

Snapshotting, history size, delta compression and ghost optimization            

 • Netcode supports snapshot history with default sizes (e.g., default snapshot 
   history size of 32 entries) and documentation recommends considerably smaller
   history sizes (e.g., 16 or 6) for MMO‑scale to reduce memory/snapshot cost.  
   Netcode implements predictive delta compression (delta‑encoding against      
   predicted baselines) that can reduce per‑ghost bandwidth but relies on       
   careful configuration of history, ack windows and interpolation times. Ghost 
   priority is computed per chunk and can be shaped by MaxSendRate, importance  
   multiplication, and relevancy settings. [30], [11], [28], [49]               

Transport layer options and driver behavior                                     

 • Netcode supports multiple NetworkDrivers (up to three simultaneously) via a  
   driver store; Unity Transport is the supported transport and has been updated
   in later Netcode releases. Plan transport buffer/queue capacities and test   
   transport behavior under high‑connection counts and unusual network          
   conditions. [48], [6]                                                        

Bandwidth & latency characteristics under large entity loads                    

 • Public estimates and community MMO optimizations show that carefully         
   compacted player state can be modest per‑update (example payloads show       
   ~61–136 bytes for player state or equipped player snapshots depending on     
   included fields) and that AOI/chunking choices (e.g., 126 m tile size and 3×3
   chunk AOI) materially affect replication counts. In practice, network traffic
   rapidly becomes the bottleneck as visible in load tests that maintained good 
   CPU utilization but hit network‑traffic limits beyond several hundred clients
   on commodity hardware. Build your planning assumptions from compact snapshot 
   budgets, strict AOI, and capped MaxSendEntities per snapshot. [22], [8]      

Client prediction and reconciliation                                            

 • Netcode for Entities includes ICommandData and a                             
   PredictedSimulationSystemGroup pattern used in sample projects to submit     
   deterministic inputs per tick and reconcile on arrival of authoritative      
   snapshots; these primitives should form the basis for client                 
   prediction/reconciliation workflows. Design prediction to be local and       
   authoritative correction to be compact and rate‑limited. [23], [11]          

Evidence gaps (networking)                                                      

 • No public, systematic bandwidth/latency benchmarks were found that show      
   Netcode for Entities serving 1k–10k active entities per world to mobile      
   clients across realistic cellular mixes and regions. Teams should run their  
   own network stress tests that include cellular emulation and varied AOI      
   scenarios.                                                                   

4. Mobile‑specific performance and battery considerations                       

CPU, memory, thermal and scheduler behavior                                     

 • Mobile optimization best practices remain required: device memory on many    
   mobile devices is constrained (1–4 GB shared CPU/GPU on many devices);       
   controlling draw calls and batching is important; Vulkan can reduce CPU      
   overhead on Android where available. Thermal throttling under sustained load 
   is a real constraint on mobile, lowering clocks and reducing throughput.     
   DOTS/Burst/Jobs give strong multithreaded throughput benefits, but DOTS      
   currently shows worse performance in very small entity counts and requires   
   systems scheduling patterns to spread load for many entities (e.g.,          
   chunk‑by‑chunk jobs or staggered work). [16], [17], [40], [18]               

Battery and sustained operation trade‑offs                                      

 • High continuous CPU threading and network activity increase battery draw and 
   induce thermal throttling; on mobile, favor adaptive tick rates, batching and
   larger interpolation windows on low‑end devices. Scheduling systems to spread
   work across frames (use Time.frameCount % interval or chunk‑per‑frame        
   processing) reduces frame spikes and sustained thermal load. [18], [31], [16]

Mobile DOTS caveats                                                             

 • Community reports note DOTS improvements are ongoing for low‑entity‑count    
   mobile scenarios and that Burst has, in some cases, caused crashes on certain
   iOS devices in the past; plan device lab testing across representative iOS   
   and Android hardware (including ARM variants), and consider feature flags to 
   disable burst or fallback to less‑aggressive scheduling on problematic       
   devices. [40], [38], [21]                                                    

Network constraints (cellular vs Wi‑Fi) and mitigations                         

 • Design for wider interpolation windows, lower per‑client tick rates for      
   mobile (tickrate slightly lower than target framerate is typical), snapshot  
   compression/delta and per‑client AOI filtering. Support adaptive tick and    
   send‑rate based on client class (low‑end mobile, high‑end mobile, Wi‑Fi) and 
   prefer batching/aggregation of non‑time‑critical events. Documented Netcode  
   options allow pausing replication per client and applying maximum replication
   distances. [31], [11], [49]                                                  

OS‑level constraints                                                            

 • The available evidence does not include explicit details on OS background    
   suspension or platform socket lifetime semantics; mobile‑specific background 
   networking/keepalive behaviour was not detailed in the reviewed sources. This
   is a material evidence gap—test app lifecycle and persistent connection      
   behavior per‑platform.                                                       

5. Operational, deployment and tooling concerns                                 

Server hosting and orchestration options                                        

 • Agones (Kubernetes game‑server controller) plus Kubernetes is a common,      
   supported pattern for containerized dedicated game servers; Agones provides  
   Helm charts and SDKs and integrates with common cloud stacks for fleet       
   autoscaling and allocation. Community examples show Netcode+Agones           
   integration for running dedicated servers in containers. Use Agones’         
   allocation and FleetAutoscaler policies to define capacity buffers for       
   spikes. [24], [19], [44], [35]                                               

Managed hosting and cost considerations                                         

 • Cloud managed services and instance pricing examples (e.g., AWS GameLift     
   instance pricing and data transfer costs) show that large fleets and data    
   transfer can be a major cost driver; case studies and cost analyses recommend
   accounting for both compute instance hours and outbound data transfer when   
   modeling MMO budgets. AKS and Kubernetes clusters have practical limits in   
   container counts and memory that must be considered when sizing clusters.    
   Cost planning must include region count, expected data egress, and fleet     
   autoscaling behavior. [36], [37], [46], [34]                                 

CI/CD, observability and debugging                                              

 • Unity Cloud Code and standard CI tooling (Docker, GitHub Actions, Jenkins,   
   Unity Build Automation) are used for automation and pipeline integration.    
   Netcode tooling received incremental tooling improvements (e.g., network     
   debugging) in later changelogs; complement Unity tooling with standard       
   container observability (Prometheus/CloudWatch), logging and custom health   
   checks for allocated GameServer pods. [25], [6]                              

Tooling for local/fleet testing                                                 

 • Community repositories and example projects (e.g., Netcode+Agones sample)    
   provide Docker/compose and local orchestration testing patterns to reproduce 
   server allocation and health checks. Include these in developer workflows to 
   validate allocation and connection patterns before cloud deployment. [19]    

Operational scaling estimates                                                   

 • Public pricing examples show that instance compute and data transfer scale   
   linearly with player count and region count; plan for data transfer to be a  
   dominant recurring cost for MMOs that replicate many entities. Also plan     
   container/cluster limits (AKS container scale guidance) when sizing          
   orchestration strategies. [36], [37], [46]                                   

Evidence gaps (operations)                                                      

 • There is no public evidence of a full production cost model for a DOTS +     
   Netcode MMO at the 1k–10k concurrent player scale with real mobile network   
   profiles; teams must run pilot deployments and collect telemetry to refine   
   operational budgets.                                                         

6. Known limitations and risks                                                  

Determinism and physics                                                         

 • Unity Physics is internally deterministic on the same hardware, but          
   floating‑point differences and SIMD/Burst optimizations can produce          
   nondeterministic results across different CPU architectures (ARM vs x86).    
   Burst does not guarantee bit‑identical arithmetic across hardware, so        
   cross‑architecture deterministic lock‑step simulations are not guaranteed    
   without careful mitigation. [20]                                             

Host migration, synchronization and runtime errors                              

 • Netcode for Entities has documented host‑migration limitations (prespawned   
   ghost sync errors, ID reallocation) and has had changelog entries and        
   community bug reports about snapshot ack windows, large server tick          
   prediction errors, and host migration crashes; these are concrete risks for  
   any production server migration or host failover path. [4], [3], [5]         

Scaling and single-thread constraints                                           

 • Community reports indicate Netcode’s server synchronization architecture has 
   historically used a single synchronization thread, and some users report     
   practical connection ceilings (e.g., ~80 active connections) depending on    
   configuration; other load tests report much higher counts (hundreds). This is
   a conflict in the evidence: both constrained (80 connections) and            
   higher‑concurrency (700+) test results exist, implying results depend heavily
   on load patterns, chunking/ghost budgets, transport tuning and hardware.     
   Treat public community numbers as indicative, not normative. [12], [8]       

Debugging and tooling challenges                                                

 • Users report that some higher‑player features (e.g., ThinClients) lack robust
   docs, and that Netcode’s debugging surface has improved but remains          
   specialized; expect a nontrivial investment in custom telemetry, in‑game     
   diagnostics and server-side debug hooks. [8], [6]                            

Security and anti‑cheat                                                         

 • Deterministic lock‑step models help resist some classes of cheating but are  
   not a panacea (map‑hacks still possible), and server‑authoritative           
   architectures remain the recommended approach for anti‑cheat. Specific       
   Netcode anti‑cheat features (server fog‑of‑war, selective replication) can   
   help, but a dedicated anti‑cheat design is still required. [11], [26]        

7. Alternatives and interoperability                                            

Open‑source and commercial alternatives                                         

 • Mirror (UNET ecosystem derivative) and Photon are prominent alternatives;    
   Mirror is recommended when a fully customizable and open networking layer is 
   required, while Photon is recommended for production‑ready managed hosting   
   and global scaling. Choose based on required ownership model, hosting        
   preferences and feature set. [14], [15]                                      

Interoperability and migration patterns                                         

 • Integration patterns include using DOTS/ECS on the simulation layer while    
   using other networking/matchmaking backends (e.g., Photon/managed services)  
   for connection brokering or using container orchestration + custom native    
   servers; community tooling (Netcode+Agones) demonstrates common integration  
   patterns for dedicated server fleets. If migration away from Netcode is      
   required, plan for decoupling of serialization and AOI logic so that the     
   higher‑level replication, snapshot and AOI logic can be mapped to a different
   transport or server framework. [19], [13]                                    

Evidence gaps (alternatives)                                                    

 • There is limited comparative benchmarking in the reviewed sources for Netcode
   for Entities versus Mirror/Photon at MMO scale on mobile clients (e.g.,      
   1k–10k entities). Teams should benchmark candidate stacks against their      
   traffic models.                                                              

8. Best practices and concrete design recommendations                           

Entity design and serialization                                                 

 • Keep replicated component sets minimal; coalesce frequently‑updated fields   
   into compact structs and use Ghost prefabs strategically. Use Netcode’s      
   fixed/dynamic buffer controls and set InternalBufferCapacity(0) on dynamic   
   buffers when appropriate to keep per‑chunk memory down. [6], [29]            

Ownership and authority                                                         

 • Give authoritative ownership to zone/region servers rather than per‑entity   
   where possible. Use Netcode’s owner concepts only for player‑controlled      
   entities and rely on server worlds to run authoritative sim and state        
   reconciliation. [11], [41]                                                   

AOI, sharding and chunk heuristics                                              

 • Use tile/chunk partitioning that maps to GhostDistanceImportance and         
   GhostDistancePartitioningSystem to reduce per‑client ghost sets; avoid       
   frequent chunk changes for entities. Choose tile sizes and AOI grids informed
   by gameplay (the example of 126 m tiles and a surrounding 3×3 AOI is one     
   concrete approach documented in MMO optimizations). Use spatial hashing/voxel
   grids implemented with DOTS jobs/Burst for neighbour searches and culling.   
   [49], [41], [42], [43], [22]                                                 

Tick and bandwidth tuning                                                       

 • Limit snapshot history sizes for MMO patterns (consider 6–16 entries instead 
   of defaults) and cap MaxSendEntities per snapshot. Use per‑client MaxSendRate
   and relevancy sets to shape bandwidth. Apply delta compression and predictive
   baseline settings to reduce traffic, and experiment with interpolation       
   windows on clients to trade latency for bandwidth. [30], [28], [11]          

Testing and benchmarking methodology                                            

 • Build incremental scale tests: (1) unit tests for                            
   serialization/deserialization and predicted reconciliation; (2) single‑server
   load tests with synthetic clients to validate per‑server ghost budgets; (3)  
   network‑conditioned tests (emulate cellular LTE/3G/Wi‑Fi jitter/loss); (4)   
   containerized fleet tests using Agones for allocation and autoscaler         
   validation. Leverage community sample repos (Netcode+Agones) for CI staging. 
   [19], [24], [23]                                                             

Example architectures (high level)                                              

 • Small‑zone authoritative servers (each zone owns entities) + edge connection 
   proxies or NAT/session brokers + central matchmaker + Agones fleet           
   autoscaler. Use chunked AOI on server and client for replication; run        
   predictive local simulation for player input; route handoffs between adjacent
   zones via compact authority transfer messages. Use Kubernetes/Agones for     
   orchestration and plan for regionally‑deployed fleets to reduce client       
   latency. [24], [35], [44], [19]                                              

9. Evidence gaps (summary)                                                      

 • No public, authoritative postmortem or benchmark that demonstrates Netcode   
   for Entities serving 1k–10k actively updated entities per world to mobile    
   clients under realistic cellular distributions and across multiple regions.  
 • No detailed, platform‑level (iOS vs Android) battery/thermal telemetry tied  
   directly to DOTS + Netcode at MMO scales in the reviewed sources.            
 • Limited cross‑technology comparative benchmarking (Netcode vs Mirror vs      
   Photon) at MMO scale on mobile devices in the available evidence.            

10. Practical takeaways and recommended next steps                              

 1 Treat Unity DOTS Entities + Netcode for Entities as a promising, actively    
   maintained foundation for a mobile MMO but plan for significant in‑house     
   engineering: pin versions, run extensive tests, and expect to consume        
   changelogs and fixes as part of normal development. [1], [3], [6], [32]      
 2 Architect for server authority by zones/partitions, combine Netcode’s chunked
   ghost relevancy with a spatial partition (spatial hashing/voxel grid) to cap 
   per‑client replication. [11], [41], [49], [42], [43]                         
 3 Prioritize network design (snapshot budgets, AOI, compression, and adaptive  
   tick) and run cellular+Wi‑Fi emulation at scale early; build synthetic load  
   tests up to and past target concurrent entity ranges. [22], [30], [8]        
 4 Use container orchestration (Kubernetes + Agones) and CI/CD automation;      
   prototype Agones allocations and autoscaler policies early and include cost  
   estimates for compute + data egress. [24], [19], [36], [37], [46]            
 5 Reserve engineering time for platform debugging and mitigation:              
   Burst/Burst‑related hardware issues, host migration edge cases, and          
   cross‑architecture determinism problems will require mitigations and platform
   testing. [38], [4], [20]                                                     
 6 If a fully managed networking/hosting option is required to reduce           
   operational complexity, evaluate alternatives (Photon, Mirror, or managed    
   hosts) and benchmark those stacks against Netcode using the team’s AOI and   
   state models. [15], [14]                                                     

Works Cited / References                                                        

[1] https://docs.unity3d.com/Packages/com.unity.entities@1.0/manual/index.html  
[2] https://unity.com/dots [3]                                                  
https://docs.unity3d.com/Packages/com.unity.netcode@1.8/changelog/CHANGELOG.html
[4]                                                                             
https://docs.unity3d.com/Packages/com.unity.netcode@1.7/manual/host-migration/ho
st-migration-limitations.html [5]                                               
https://discussions.unity.com/t/netcode-for-entities-large-servertick-error/1687
922 [6]                                                                         
https://docs.unity3d.com/Packages/com.unity.netcode@1.11/changelog/CHANGELOG.htm
l [7]                                                                           
https://discussions.unity.com/t/ecs-development-status-december-2025/1699284 [8]
https://discussions.unity.com/t/700-concurrent-users-in-mmo-style-tech-demo-with
-netcode-for-entities/951434 [9]                                                
https://crearevideogiochi.it/wp-content/uploads/2024/07/Data-Oriented_Technology
_Stack_for_advanced_Unity_users_2022_LTS_final.pdf [10]                         
https://discussions.unity.com/t/dots-development-status-and-next-milestones-june
-2023/919381 [11]                                                               
https://docs.unity3d.com/Packages/com.unity.netcode@1.6/manual/optimizations.htm
l [12] https://discussions.unity.com/t/dots-netcode-for-mmorpg/785495 [13]      
https://discussions.unity.com/t/dots-best-practices-guide-1-0-is-here/912107?pag
e=2 [14]                                                                        
https://discussions.unity.com/t/what-are-the-pros-and-cons-of-available-network-
solutions-assets/727663 [15]                                                    
https://uversedigital.com/blog/unity-netcode-vs-mirror-vs-photon/ [16]          
https://generalistprogrammer.com/tutorials/unity-mobile-game-optimization-comple
te-guide [17]                                                                   
https://uhiyama-lab.com/en/notes/unity/unity-mobile-game-optimization-guide/    
[18] https://discussions.unity.com/t/dots-frequency-scheduling/797756 [19]      
https://github.com/mbychkowski/unity-netcode-agones [20]                        
https://discussions.unity.com/t/is-unity-dots-physics-deterministic/906632 [21] 
https://discussions.unity.com/t/example-of-distance-interest-management-like-mir
ror-s-proximity-checker-in-netcode-for-entities/1681368 [22]                    
https://wirepair.org/2025/12/20/netcode-optimizations-for-mmorpgs/ [23]         
https://discussions.unity.com/t/unity-netcode-for-entities-how-to-implement-icom
manddata-input-client-prediction-for-rts/1673572 [24]                           
https://agones.dev/site/docs/installation/install-agones/helm/ [25]             
https://docs.unity.com/en-us/cloud-code/modules/how-to-guides/automation [26]   
https://discussions.unity.com/t/is-cheating-modding-possible-with-dots/806093   
[27]                                                                            
https://discussions.unity.com/t/multiplayer-ecs-dots-with-about-450-players-per-
scene/839844 [28]                                                               
https://docs.unity3d.com/Packages/com.unity.netcode@1.9/manual/optimization/opti
mize-ghosts.html [29]                                                           
https://docs.unity3d.com/Packages/com.unity.netcode@1.2/manual/optimizations.htm
l [30]                                                                          
https://docs.unity3d.com/Packages/com.unity.netcode@6.5/manual/optimization/limi
t-snapshot-size.html [31]                                                       
https://discussions.unity.com/t/best-clientservertickrate-config-for-mobile-game
s-30-60-fps/888113 [32]                                                         
https://discussions.unity.com/t/dots-development-status-and-milestones-ecs-for-a
ll-september-2024/1519286 [33] https://unity.com/case-study/zenith-the-last-city
[34]                                                                            
https://aws.amazon.com/blogs/gametech/developers-guide-to-operate-game-servers-o
n-kubernetes-part-2/ [35]                                                       
https://tavant.com/blog/agones-kubernetes-centric-game-server-toolkit/ [36]     
https://aws.amazon.com/gamelift/servers/pricing/ [37]                           
https://learn.microsoft.com/en-us/azure/aks/cost-analysis [38]                  
https://discussions.unity.com/t/professional-service-dots-porting-postmortem/830
336 [39]                                                                        
https://discussions.unity.com/t/unity-dots-case-study-in-production/716170?page=
3 [40] https://discussions.unity.com/t/ecs-code-overhead-for-mobile-games/746059
[41]                                                                            
https://docs.unity3d.com/Packages/com.unity.netcode@1.3/api/Unity.NetCode.html  
[42] https://remex.me/projects/gea-spatial-partition [43]                       
https://github.com/Sylmerria/Spatial-Hashing [44]                               
https://agones.dev/site/docs/integration-patterns/player-capacity/ [45]         
https://discussions.unity.com/t/dots-get-in-touch-with-the-teams-behind-it/91739
7?page=2 [46] https://edgegap.com/blog/the-hidden-cost-of-aws-gamelift-s-pricing
[47]                                                                            
https://docs.unity3d.com/Packages/com.unity.netcode@1.4/manual/client-server-wor
lds.html [48]                                                                   
https://docs.unity3d.com/Packages/com.unity.netcode@1.6/manual/networking-networ
k-drivers.html [49]                                                             
https://docs.unity3d.com/Packages/com.unity.netcode@1.4/manual/optimizations.htm
l                                                                               

                                  Sources (49)                                  
┏━━━━━━┳━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┳━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┓
┃ #    ┃ Title                             ┃ URL                               ┃
┡━━━━━━╇━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━╇━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┩
│ 1    │ Entities overview | Entities |    │ https://docs.unity3d.com/Package… │
│      │ 1.0.16 - Unity - Manual           │                                   │
│ 2    │ Unity's Data-Oriented Technology  │ https://unity.com/dots            │
│      │ Stack (DOTS)                      │                                   │
│ 3    │ [1.8.0] - 2025-08-17 | Netcode    │ https://docs.unity3d.com/Package… │
│      │ for Entities - Unity - Manual     │                                   │
│ 4    │ Limitations and known issues |    │ https://docs.unity3d.com/Package… │
│      │ Netcode for Entities | 1.7.0      │                                   │
│ 5    │ Netcode for Entities Large        │ https://discussions.unity.com/t/… │
│      │ serverTick error - Unity          │                                   │
│      │ Discussions                       │                                   │
│ 6    │ [1.11.0] - 2025-12-12 | Netcode   │ https://docs.unity3d.com/Package… │
│      │ for Entities - Unity - Manual     │                                   │
│ 7    │ ECS Development Status - December │ https://discussions.unity.com/t/… │
│      │ 2025 - Unity Engine               │                                   │
│ 8    │ 700 concurrent users in MMO-style │ https://discussions.unity.com/t/… │
│      │ tech demo with Netcode for        │                                   │
│      │ Entities                          │                                   │
│ 9    │ [PDF] Introduction to the         │ https://www.crearevideogiochi.it… │
│      │ Data-Oriented Technology Stack    │                                   │
│      │ for advanced ...                  │                                   │
│ 10   │ Optimizing your game | Netcode    │ https://docs.unity3d.com/Package… │
│      │ for Entities | 1.6.2 - Unity -    │                                   │
│      │ Manual                            │                                   │
│ 11   │ DOTS development status and next  │ https://discussions.unity.com/t/… │
│      │ milestones - June 2023            │                                   │
│ 12   │ DOTS NetCode for MMORPG - Unity   │ https://discussions.unity.com/t/… │
│      │ Engine                            │                                   │
│ 13   │ DOTS Best Practices Guide 1.0 is  │ https://discussions.unity.com/t/… │
│      │ here - Page 2                     │                                   │
│ 14   │ What are the pros and cons of     │ https://discussions.unity.com/t/… │
│      │ available network                 │                                   │
│      │ solutions/assets                  │                                   │
│ 15   │ Unity Netcode vs Mirror vs Photon │ https://uversedigital.com/blog/u… │
│      │ – Multiplayer Frameworks          │                                   │
│ 16   │ Unity Mobile Game Optimization:   │ https://generalistprogrammer.com… │
│      │ Complete Performance Guide (2025) │                                   │
│ 17   │ DOTS Frequency Scheduling - Unity │ https://discussions.unity.com/t/… │
│      │ Engine                            │                                   │
│ 18   │ 【Unity】Unity Mobile Game        │ https://uhiyama-lab.com/en/notes… │
│      │ Optimization Guide                │                                   │
│ 19   │ mbychkowski/unity-netcode-agones  │ https://github.com/mbychkowski/u… │
│      │ - GitHub                          │                                   │
│ 20   │ Is unity dots physics             │ https://discussions.unity.com/t/… │
│      │ deterministic?                    │                                   │
│ 21   │ Limit snapshot size | Netcode for │ https://docs.unity3d.com/Package… │
│      │ Entities | 6.5.0 - Unity - Manual │                                   │
│ 22   │ Example of Distance Interest      │ https://discussions.unity.com/t/… │
│      │ Management (like Mirror's ...     │                                   │
│ 23   │ Netcode optimizations for MMORPGs │ https://wirepair.org/2025/12/20/… │
│      │ - a place to jot                  │                                   │
│ 24   │ Unity Netcode for Entities How to │ https://discussions.unity.com/t/… │
│      │ implement ...                     │                                   │
│ 25   │ Install Agones using Helm         │ https://agones.dev/site/docs/ins… │
│ 26   │ Integrate with CI/CD • Cloud Code │ https://docs.unity.com/en-us/clo… │
│      │ • Unity Docs                      │                                   │
│ 27   │ Is Cheating / Modding possible    │ https://discussions.unity.com/t/… │
│      │ with dots? - Unity Engine         │                                   │
│ 28   │ Multiplayer ECS/DOTS with about   │ https://discussions.unity.com/t/… │
│      │ 450 players per Scene             │                                   │
│ 29   │ Ghost optimization | Netcode for  │ https://docs.unity3d.com/Package… │
│      │ Entities | 1.9.3 - Unity - Manual │                                   │
│ 30   │ Optimizations | Netcode for       │ https://docs.unity3d.com/Package… │
│      │ Entities | 1.2.2-pre.1 - Unity -  │                                   │
│      │ Manual                            │                                   │
│ 31   │ best ClientServerTickRate config  │ https://discussions.unity.com/t/… │
│      │ for mobile games (30/60 fps)      │                                   │
│ 32   │ DOTS development status and       │ https://discussions.unity.com/t/… │
│      │ milestones + ECS for all          │                                   │
│      │ (September ...                    │                                   │
│ 33   │ Creating VR MMO Zenith: The Last  │ https://unity.com/case-study/zen… │
│      │ City                              │                                   │
│ 34   │ Developer's Guide to operate game │ https://aws.amazon.com/blogs/gam… │
│      │ servers on Kubernetes – Part 2    │                                   │
│ 35   │ Agones: A Kubernetes-Centric      │ https://tavant.com/blog/agones-k… │
│      │ Toolkit for Scaling Game Servers  │                                   │
│ 36   │ Professional Service DOTS-Porting │ https://discussions.unity.com/t/… │
│      │ Postmortem - Unity Discussions    │                                   │
│ 37   │ Unity DOTS case study in          │ https://discussions.unity.com/t/… │
│      │ production - Page 3               │                                   │
│ 38   │ ECS code overhead for mobile      │ https://discussions.unity.com/t/… │
│      │ games                             │                                   │
│ 39   │ Namespace Unity.NetCode | Netcode │ https://docs.unity3d.com/Package… │
│      │ for Entities | 1.3.2              │                                   │
│ 40   │ Game Architecture: Spatial        │ https://www.remex.me/projects/ge… │
│      │ Partition - Unity DOTS - Hunter   │                                   │
│      │ Carlson                           │                                   │
│ 41   │ Spatial hashing for Unity using   │ https://github.com/Sylmerria/Spa… │
│      │ ECS/DOTS - GitHub                 │                                   │
│ 42   │ Allocating based on GameServer    │ https://agones.dev/site/docs/int… │
│      │ Player Capacity - Agones          │                                   │
│ 43   │ Dedicated Game Server Hosting -   │ https://aws.amazon.com/gamelift/… │
│      │ Amazon GameLift Pricing           │                                   │
│ 44   │ Azure Kubernetes Service (AKS)    │ https://learn.microsoft.com/en-u… │
│      │ cost analysis - Microsoft Learn   │                                   │
│ 45   │ DOTS: get in touch with the teams │ https://discussions.unity.com/t/… │
│      │ behind it! - Page 2 - Unity       │                                   │
│      │ Engine                            │                                   │
│ 46   │ The hidden cost of AWS Gamelift's │ https://edgegap.com/blog/the-hid… │
│      │ pricing                           │                                   │
│ 47   │ Client and server worlds          │ https://docs.unity3d.com/Package… │
│      │ networking model | Netcode for    │                                   │
│      │ Entities | 1.4.1                  │                                   │
│ 48   │ Netcode for Entities multi-driver │ https://docs.unity3d.com/Package… │
│      │ architecture                      │                                   │
│ 49   │ Optimizing your game | Netcode    │ https://docs.unity3d.com/Package… │
│      │ for Entities | 1.4.1 - Unity -    │                                   │
│      │ Manual                            │                                   │
└──────┴───────────────────────────────────┴───────────────────────────────────┘

───────────────────────────── 49 sources | 444.05s ─────────────────────────────
