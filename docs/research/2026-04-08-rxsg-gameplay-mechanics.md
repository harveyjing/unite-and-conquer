
  Research Report

Research report: Gameplay mechanics, progression and systems of 热血三国 (Re Xue
                       San Guo / Passion Three Kingdoms)                        

Executive summary                                                               

热血三国 is a long‑running Chinese SLG (strategy + city‑building) that combines 
city development, multi‑city technology, deep general/gear systems, unit        
formation/rock‑paper‑scissors combat, and a persistent 500×500 world grid with  
alliance warfare and cross‑server events. The design emphasizes city            
specialization (resource vs. military), extensive equipment and general         
collection, large‑scale siege requirements, and social features (alliances,     
transport/raid mechanics). The game has evolved across versions since its 2008  
launch with additional equipment tiers, high‑rarity items and recurring events; 
revenue mechanics (VIP, purchasable acceleration items, jewelry for recruiting  
high‑tier generals) are built tightly into progression loops. The sections below
synthesize primary Chinese sources, community guides and official notes.        

Contents                                                                        

 • Overview and scope                                                           
 • City building                                                                
    • Building types and upgrade caps                                           
    • City layout and founding constraints                                      
    • Housing / population mechanics and tax interactions                       
    • Building queues and acceleration                                          
 • Resource systems                                                             
    • Resource types, production and caps                                       
    • Gatherable nodes vs. steady income; raiding and transport                 
    • Storage, resource sinks and trade/exchange mechanics                      
 • Generals (heroes)                                                            
    • Recruitment and collection                                                
    • Rarity/tier system and jewelry costs                                      
    • Attributes, skills and item/consumable buffs                              
    • Leveling, promotion, awakening and special items                          
    • Equipment / gear system                                                   
    • Team composition and synergies                                            
 • Units and combat mechanics                                                   
    • Unit types and counters                                                   
    • Formations and tactical relationships                                     
    • Combat resolution, range and damage formulas                              
    • Siege units, walls and tactical controls                                  
 • World map structure and march mechanics                                      
 • Alliances and social systems                                                 
 • PvP, arena and siege rules                                                   
 • Progression and pacing                                                       
 • Monetization, gacha and events                                               
 • Distinctive features compared with other Three Kingdoms SLGs                 
 • Evidence gaps                                                                
 • References                                                                   

Overview and scope                                                              

This report synthesizes official documentation, long‑running community guides   
and up‑to‑date game‑system writeups to cover the game systems requested. Where  
facts are missing in the available evidence, the report explicitly notes those  
gaps. The report focuses on the core mainland Chinese release and related       
Chinese‑language resources; it includes mechanics documented in community guides
and official patch/news pages. [4]                                              

--------------------------------------------------------------------------------

City building                                                                   

Building types and upgrade caps                                                 

Cities include resource producers (farms/fields, lumber mills, quarries/stone,  
iron mines), internal buildings (academy, warehouses, market, training grounds, 
arrow towers, walls, talent halls, post stations) and alliance buildings. Named 
cities (county/commandery/state/capital) impose different level caps for        
resource buildings and internal buildings; the game shows explicit level caps   
and scaling in its build tables. Building cost and production scale with level, 
and some versions publish cost tables for reference. [20], [30]                 

City layout and founding constraints                                            

New city founding is constrained to plain terrain: only plain cells can be used 
to found a city; other terrain types (forest, mountain, swamp, lake, desert,    
grassland) exist on the world grid and provide specific occupancy bonuses but do
not permit city founding. Founding a new city requires an available flat/plain  
tile, a minimum official rank, and an assigned general; founding costs include  
lump sums of the basic resources and gold. Cities start with a limited number of
building slots (a low starter number) and can expand slots as 官府 (city        
hall/governance) level increases up to a maximum number of slots (documented    
progression to a maximum). The world is presented in a 2.5D large‑view grid.    
[31], [18]                                                                      

Housing / population mechanics and tax interactions                             

Population (民口) is a central mechanical variable. Population caps and         
population growth interact with tax rate: higher tax increases immediate gold   
income but suppresses population growth; lower tax increases population growth  
and thus long‑term tax base. Documented formulas include a population‑growth    
rule (population growth per hour = population cap ÷ 20) and tax inputs that     
change the growth rate; recommended population management strategies (resource  
city vs. military city) are community‑recorded. Specific caps and example values
have been reported (e.g., example population cap ~19,300 with corresponding     
hourly gold production reported in community calculators). Maintaining and      
expanding storage and population is necessary to avoid automatic resource loss  
("disaster" or overflow mechanics). [28], [1], [5]                              

Building queues and acceleration                                                

Construction follows standard SLG build/upgrade queues; players may accelerate  
construction with consumables and premium purchases. Historically, a purchasable
labor/order item (example: 50 元宝 item) enabled building multiple structures   
simultaneously (five structures) for a multi‑day window, a direct paid shortcut 
to increasing effective build concurrency. Research levels and certain general  
attributes also reduce build times. Technology levels and specific general      
internal administration attributes apply construction‑time reductions as        
percentage modifiers. [3], [22]                                                 

--------------------------------------------------------------------------------

Resource systems                                                                

Resource types, production and caps                                             

Primary resource categories are food (grain), wood, stone and iron produced by  
dedicated resource buildings. Gold (tax) is a separate economy derived from city
taxes. Named cities (county/commandery/state) have much higher production       
ceilings; community references give example per‑hour production estimates for   
named cities (for example approximate food production per hour for              
county/commandery/state levels have been published). Technology and some        
general/stat bonuses increase resource production and storage capacity. [27],   
[22], [1]                                                                       

Gatherable nodes vs. steady income; raiding and transport                       

Steady income comes from city resource buildings; the world contains            
wilderness/wildland tiles that can be scouted, occupied or raided. Raiding      
wilderness yields loot determined by the target city's resources and wilderness 
bonus — community guides document the raid calculation dependency on the        
opponent’s main city resource amount and wilderness multiplier. Transporting    
resources to allied cities consumes grain (transport cost) and risks NPC        
robberies en route; allied transport also consumes extra grain and has theft    
risk. Wild tiles vary in level (1–10) and change over time (unoccupied tiles    
tend to level up daily while occupied tiles decline). [2], [19], [18]           

Storage, resource sinks and trade/exchange mechanics                            

Warehouses and markets both protect stored resources but in different contexts: 
warehouses protect against losses while the player is offline, while markets    
provide protection during active play. Upgrading storage buildings increases    
capacity and mitigates resource "overflow disasters"; some in‑game items (e.g., 
"清仓令") were introduced to auto‑clear excess resources to avoid disaster.     
Markets also enable intra‑city trading/market features and act as a             
trade/protection sink. Common resource sinks include troop recruitment/training,
construction, research and disaster relief mechanics that can consume large sums
(community examples include high periodic disaster relief consumption). [5],    
[6], [1]                                                                        

--------------------------------------------------------------------------------

Generals (heroes)                                                               

Recruitment and collection methods                                              

Generals are collected via capture mechanics and recruitment consumables.       
High‑tier (god/name) generals require specific jewelry items to recruit         
(examples: 夜明珠, 玉石, 翡翠 for god generals; 玛瑙 and 珊瑚 for name          
generals), and community guides document jewelry costs required per tier        
(typical thresholds published for 80/90/100‑level god generals). Some powerful  
generals are obtainable through county/commandery captures requiring rank       
thresholds in those locations. [8], [17]                                        

Rarity / tier system                                                            

Generals are categorized into tiers (regular/name/god) with god generals further
subdivided (example community delineation: 80/90/100‑level god generals         
corresponding to jewelry costs and expected battle power). Recruitment costs    
scale by tier; high tiers grant substantially higher base stats and growth. [8],
[17]                                                                            

Attributes, skills and consumable buffs                                         

Generals have a broad attribute set including stamina (体力), energy (精力),    
command/leadership (统率), speed, attack, defense, salary, loyalty, internal    
affairs (内政), bravery (勇武), intelligence (智谋) and potential. General      
internal (内政) contributes directly to resource bonuses and construction speed 
reductions when assigned to city roles; individual items and consumables exist  
that globally buff generals’ dimensions (lists of named consumables that        
increase courage, intelligence, command, internal, attack, defense, stamina,    
speed and specific unit stats are documented). [28], [13], [3]                  

Leveling and promotion                                                          

General experience follows explicit community‑documented formulas: experience   
needed to level = current_level^2 × 100. Experience sources include consumable  
experience items, assignment to duties (city guard), building/research actions  
and battle participation (with battle grants up to a fraction of needed         
experience). Leveling yields potential/attribute point gains usable to customize
generals. [14]                                                                  

Awakening, fusion and special items                                             

Special relics and items (examples: 灵兽玉玺) exist with orange/red rarities and
refinement systems; refining requires fragments in growing quantities per star  
level, and high‑rarity variants enable carving of superior attribute tags. These
mechanics function as long‑term enhancement systems distinct from equipment,    
used to further increase a general’s specific attributes. [16]                  

Equipment and gear                                                              

Equipment has multiple quality tiers ranging from                               
gray/white/green/blue/purple/orange to later introduced red tiers; equipment    
parts are numerous (documentation cites up to 16 equipment slots per general:   
armor, accessories, weapons and mount slots). Equipment levels correlate to     
character level brackets (examples: levels 10/30/50/80/100). Some named sets    
(神武, 神龙, 冰魂, 焚天, 名将套装) grant large set bonuses; certain set upgrades
require special materials and have non‑guaranteed success rates in              
upgrade/upgrade paths. Equipment acquisition methods include event sign‑ins,    
elite dungeons, exchange activities, ranking rewards and shop purchases.        
Equipment also has secondary attribute slots and set upgrade/slotting mechanics.
[9], [10]                                                                       

Team composition and synergies                                                  

World‑war mechanics limit the number of main generals allowed in certain large  
events (the main general is prioritized and is the only main general allowed in 
world wars); players are advised to prioritize upgrading that main slot and its 
equipment. Consumable and set bonuses add unit or global class buffs (examples: 
items that add base attributes to unit classes or increase range/speed), and    
specialized general specialties (城战, 医护, 步兵, 骑兵, 近战, 器械, 精兵, 警备)
add class/role bonuses in battle. [8], [13]                                     

--------------------------------------------------------------------------------

Unit types and combat mechanics                                                 

Unit classes and counters                                                       

Troops include infantry varieties (spear/long‑spear, archer,                    
shield/knife‑shield), cavalry varieties (light, heavy/iron, archer‑cavalry), and
siege/engine units (crossbows/床弩, battering rams/冲车, trebuchets/投石车) plus
specialty regional units. Community data documents their roles and counters     
(e.g., spearmen strong vs cavalry; archers weak on defense but counter          
spearmen/horse‑archer types; shield troops excel in defense and counter ranged).
Siege engines have specialized roles: battering rams for walls, trebuchets for  
defensive structures and area damage, crossbows with high piercing. [11], [23]  

Tactical counters and numeric modifiers                                         

Published counter multipliers and examples exist in community handbooks: e.g.,  
gun (spear) vs cavalry 180% damage, archer vs shield 70%, shield vs gun 110%,   
cavalry vs archer 120%, siege engine vs wall ~150% — community guides enumerate 
these counter multipliers as tactical design rules that underpin army           
composition. [23]                                                               

Formations and rock‑paper‑scissors                                              

The game documents multiple formations with deterministic relationships (listed 
formation beats relationships — e.g., 锥形 beats 长蛇; 雁行 beats 锥形; 鱼鳞    
beats 雁行; 鹤翼 beats 鱼鳞; and so on in a chain). Formation choice is a       
mechanical layer above raw unit types and influences battle outcome in a        
cyclical advantage system. [12]                                                 

Combat resolution, range and damage formulas                                    

Combat is resolved by the game’s simulation engine (documentation and community 
analysis provide formula fragments rather than a full public engine spec).      
Notable formulas recorded by community guides include:                          

 • Remote troop attack calculation example: (remote attack ÷ 2) × (1 +          
   technology + general's attack ÷ 100).                                        
 • Damage resolution example: damage ≈ (attack × attack) ÷ (attack + defense);  
   special "full‑damage" attacks can double that value.                         
   Additionally, battle distance is calculated from the longest‑range unit's    
   range plus a fixed offset (documented in guides). These formulas are         
   representative extracts from community reverse‑engineering and guide material
   rather than a published comprehensive formula set. [15], [24]                

Morale / stamina and tactical controls                                          

Generals have stamina/energy attributes and loyalty mechanics; low loyalty can  
lead to defections or capture risk. Consumables can increase stamina and speed; 
recommended battle preparations include prebattle items (battle drums, tactical 
maps, skill items) and certain battle skills specialized for sieges and         
multi‑unit battlefield control. Full real‑time player tactical inputs beyond    
formation and prebattle skill/item selection are not explicitly documented in   
the sources. [8], [13], [28]                                                    

--------------------------------------------------------------------------------

World map structure and march mechanics                                         

The persistent world map is a 500×500 grid with coordinates and a 2.5D view;    
players spawn randomly within a chosen state (province) among 13 states.        
Distance between cells directly affects transport and march/travel times; the   
client includes map navigation tools such as centering, coordinate jump and     
bookmarking. Wild lands have terrain types and level progression (unoccupied    
tiles level up daily while occupied tiles level down). Named (NPC/player) cities
occupy cells and have flags whose colors indicate diplomatic status granting or 
restricting actions (transport, scout, raid, occupy, tactics, letters). Scouting
reveals defending troop info and online status to various degrees depending on  
scouting level. March speed and unit movement can be modified by technology     
(examples: tech levels increase infantry march speed by 10%/level and cavalry by
5%/level in the recorded technology system). Battle distance and event          
scheduling (e.g., cross‑server events) are map‑aware mechanics. [18], [19],     
[24], [22]                                                                      

--------------------------------------------------------------------------------

Alliances and social systems                                                    

Alliances are created via a specific building (鸿胪寺): level 1 enables joining 
and level 2 permits creation. Alliances share technologies and can transfer     
resources; alliance members can send letters, transport resources (with         
transport risk) and assist in battles. Diplomatic states (friendly, allied,     
neutral, enemy, declared war) are visualized via city flags and directly        
determine permitted actions. The game also contains a text‑based "stranger"     
social system and long‑term alliance tech progression. Official notes and       
community guides show alliance declaration/war mechanics and cross‑alliance     
interactions (including transport and assistance features). [18], [21], [7]     

--------------------------------------------------------------------------------

PvP, arena and siege mechanics                                                  

Open‑world PvP and declarations                                                 

The world supports open‑world PvP actions (scout, raid, occupy, declare war)    
with flag states affecting permitted actions; alliances can declare war on other
alliances, enabling multi‑city battles and occupation. War declarations and     
diplomacy change permissible interactions on the map. [18]                      

Arena / duel systems                                                            

The arena provides a daily free challenge allowance (example: 10 free           
challenges/day) with purchasable extra attempts via premium currency; rewards   
include resources, prestige and ranking rewards based on nightly positions.     
Cross‑server and timed events exist (e.g., a cross‑server event schedule for    
evening hours) to drive competitive play. [25], [24]                            

Siege mechanics, walls and defenses                                             

Siege warfare requires specialized army composition and siege engines. Community
guides recommend large army sizes for effective sieges (example                 
minimum/composition guidelines and recommended total army size around 1.5       
million troops for full sieges, adjusted upward if loyalty is low). Walls and   
defensive structures (arrow towers, traps, rejection devices) contribute        
defensive bonuses and affect ranged unit behavior (documented increases to      
ranged unit range and structure ranges). Siege engines (冲车 for wall‑breaking, 
投石车 for area/structure damage, 床弩 for piercing) have explicit counterplay  
and defined roles in siege battles. Market/warehouse protections and city       
defense structures affect resource losses and capture penalties. [8], [11],     
[27], [5]                                                                       

--------------------------------------------------------------------------------

Progression and pacing                                                          

Typical power curve and caps                                                    

Progression is driven by city upgrades, technology research and                 
general/equipment advancement. Technology provides per‑level percentage bonuses 
(example per‑level 10% increases to resource production, storage capacity and   
construction speed on certain branches; attack/defense bonuses in other         
branches), making tech a significant long‑term multiplier. Named cities raise   
production ceilings and troop caps. Player power growth is gated by resource    
acquisition, recruitment materials (jewelry, gear), and the ability to          
obtain/upgrade equipment sets. Large‑scale PvP (sieges, cross‑server events)    
marks endgame objectives requiring substantial resource and recruitment         
investment. [22], [20], [27]                                                    

Midgame and endgame objectives                                                  

Midgame goals include expanding to named cities, assembling and equipping a     
siege‑capable army, developing alliance tech and completing collection of       
mid‑tier sets/gear. Endgame objectives concentrate on large‑scale alliance wars,
cross‑server ranking events and owning or contesting high‑value named cities.   
Events and ranking ladders create recurring endgame targets. [24], [20]         

Progression gates and grind loops                                               

Progress is gated by: rank/official position requirements for capturing certain 
generals or founding cities; jewelry and premium consumables required to recruit
high‑tier generals; material and event‑driven acquisition of top equipment sets 
and set upgrades with low success chances for some upgrades. These gated        
resources create repeating grind loops (farm resources/participate in events →  
obtain materials/jewelry → recruit/upgrade generals or equipment → participate  
in higher‑tier PvP). [8], [9], [6]                                              

--------------------------------------------------------------------------------

Monetization, gacha mechanics and events                                        

Monetization interfaces                                                         

Monetization includes VIP systems granting privileges, purchasable acceleration 
items (labor orders that temporarily increase build concurrency), premium       
currency purchases for extra arena attempts, and item/gear shops that include   
high‑tier set purchases. Age‑appropriate payment limits are enforced per account
(official restrictions on minors and specific daily/monthly caps exist). Events 
and ranking rewards often provide gear fragments, jewelry or event‑only purchase
opportunities. [20], [3], [25], [7], [6]                                        

Gacha / recruitment interplay                                                   

High‑tier general recruitment is tied to consumable items (jewelry) functioning 
as gacha currency; different jewelry rarities map to different general pools.   
The jewelry cost thresholds for higher god tiers are published in community     
guides (examples: 80/90/100‑level god generals require increasing jewelry       
costs). This ties a direct monetized resource (jewelry acquisition routes) into 
power acquisition. [8], [17]                                                    

Events and meta‑economy                                                         

Recurring events (sign‑in, elite dungeons, exchange shops, cross‑server         
competitions and historic event chains such as "黄巾之乱") supply upgrade       
materials, set pieces and recruitment items, shaping the meta and providing     
non‑monetary acquisition paths for core progression resources. Some rare sets   
have specific acquisition windows via events or long‑term login rewards. [6],   
[9], [24]                                                                       

--------------------------------------------------------------------------------

Distinctive features of 热血三国                                                

 • Persistent 500×500 grid 2.5D world with state selection and terrain that     
   levels over time, enabling long‑term territorial play and wild node dynamics 
   uncommon in simpler city builders. [18]                                      
 • Deep equipment architecture: large per‑general slot counts (documented 16    
   slots), many set‑upgrade mechanics and late‑introduced red‑tier equipment    
   with new attribute types — more elaborate gear progression than many         
   comparators. [9], [10]                                                       
 • A layered combat system: wide unit type taxonomy + formation cyclical        
   advantages + explicit numeric counter multipliers giving players both macro  
   (army composition, siege engines) and micro (formation choice, prebattle     
   items) levers. [11], [12], [23]                                              
 • Multi‑city and shared tech design: academy/technology sharing across owned   
   cities once developed to certain levels encourages multi‑city progression    
   rather than single‑city focus. [21]                                          
 • Long history and iterative content: original 2008 launch with                
   sequels/iterations and continuous content (events, new gear tiers), resulting
   in a deep legacy community and operator‑driven event cadence. [29]           
 • Social/operational UX elements: map bookmarking, coordinate teleportation,   
   text stranger system and detailed flag/diplomacy UI provide affordances for  
   alliance coordination and persistent warfare. [18], [7]                      

--------------------------------------------------------------------------------

Evidence gaps                                                                   

The reviewed sources are informative but leave several areas underspecified or  
undocumented in the available evidence:                                         

 • The client/server exact combat resolution engine and full published damage   
   formula suite beyond the community fragments provided (only partial formulas 
   and examples are documented). [15]                                           
 • Real‑time versus turn‑based micro‑control scope during live combat (beyond   
   formation/skill/prebattle choices) is not explicitly stated in the evidence; 
   community guides imply simulated resolution but do not present a definitive  
   developer statement. [8], [15]                                               
 • Exact numerical progression curves for most building/resource/tech levels    
   across all named city types are only available via occasional cost tables — a
   comprehensive official level→cost→production table was not supplied in the   
   selected findings (some snapshots exist). [30]                               
 • Precise cross‑region/platform differences (mainland/overseas mobile/web      
   forks) are not fully documented in the reviewed material; most citations     
   reflect the mainland/web lineage and community guides. [29]                  

--------------------------------------------------------------------------------

References                                                                      

[1] https://web.4399.com/rxsg/wjgl_23_3290.html                                 
[2] https://ledu.com/sg/wiki/wiki107.html                                       
[3] https://web.17173.com/content/2009-01-04/1231065869,1.shtml                 
[4] https://web.4399.com/rxsg/yxzy/xszn/                                        
[5] https://web.4399.com/rxsg/wjgl_16_2865.html                                 
[6] https://91wan.com/rxsg/youxigonggao/2008-08/20645.html                      
[7] https://web.4399.com/rxsg/news_28_63037.html                                
[8] https://gamersky.com/handbook/201609/814853_4.shtml                         
[9] https://cnblogs.com/betaOrionis/p/18456450                                  
[10] https://gamersky.com/handbook/201608/793906.shtml                          
[11] https://web.4399.com/rxsg3/yxgl/a773605.html                               
[12] https://gamersky.com/handbook/201610/819081.shtml                          
[13] https://web.4399.com/rxsg/yxzy/gsjj/a1204153.html                          
[14] https://web.4399.com/rxsg/wjgl_27_17197.html                               
[15] https://ledu.com/sg/dyjh/answer456.html                                    
[16] https://sg.ledu.com/news/2024-09-24/44863.html                             
[17] https://gamersky.com/handbook/201609/807635.shtml                          
[18] https://91wan.com/rxsg/junshijianshe/2008-09/20668.html                    
[19] https://91wan.com/rxsg/mcmjyouxiziliao/2008-11/20738.html                  
[20] https://web.4399.com/rxsg/news_12_1458.html                                
[21] https://web.4399.com/rxsg/wjgl_16_7814.html                                
[22] https://web.4399.com/rxsg/yxzl/kjfz_30_776.html                            
[23] https://gamersky.com/handbook/201609/808229.shtml                          
[24] https://ledu.com/sg/wiki/wiki587.html                                      
[25] https://gamersky.com/handbook/201608/792809.shtml                          
[26] https://web.4399.com/rxsg/news_11_86025.html                               
[27] https://3kingdom.com.my/sg3/intro/9                                        
[28] https://baike.baidu.com/item/%E7%83%AD%E8%A1%80%E4%B8%89%E5%9B%BD/1872072  
[29] https://gamersky.com/handbook/201609/811882.shtml                          
[30] https://web.4399.com/rxsg/yxzl_23_991724.html                              
[31] https://web.4399.com/rxsg/wjgl_04_19243.html                               

                                  Sources (30)                                  
┏━━━━━━┳━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┳━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┓
┃ #    ┃ Title                             ┃ URL                               ┃
┡━━━━━━╇━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━╇━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┩
│ 1    │ 4399热血三国人口上限，资源加成与… │ http://web.4399.com/rxsg/wjgl_23… │
│ 2    │ 热血三国                          │ https://www.ledu.com/sg/wiki/wik… │
│ 3    │ [热血三国]新手完全攻略——玩家稿件… │ https://web.17173.com/content/20… │
│ 4    │ 新手指南_热血三国 - 4399网页游戏  │ https://web.4399.com/rxsg/yxzy/x… │
│ 5    │ 热血三国分城建设浅析              │ http://web.4399.com/rxsg/wjgl_16… │
│ 6    │ 4399热血三国深入探讨分城建设      │ http://web.4399.com/rxsg/wjgl_04… │
│ 7    │ 4399《热血三国》版本更新公告      │ https://web.4399.com/rxsg/news_2… │
│ 8    │ 《热血三国》1.1.07版本更新内容介… │ http://www.91wan.com/rxsg/youxig… │
│      │ - 网页游戏                        │                                   │
│ 9    │ 《热血三国3》最全武将攻略名将神 … │ https://www.gamersky.com/handboo… │
│      │ - 游民星空                        │                                   │
│ 10   │ 热血三国装备图鉴（一）：灰色、白… │ https://www.cnblogs.com/betaOrio… │
│      │ - 博客园                          │                                   │
│ 11   │ 《热血三国3》装备系统武将装备的 … │ https://www.gamersky.com/handboo… │
│      │ - 游民星空                        │                                   │
│ 12   │ 兵种分析兵种详情与兵种相克-       │ https://web.4399.com/rxsg3/yxgl/… │
│      │ 热血三国3 - 4399网页游戏          │                                   │
│ 13   │ 《热血三国3》阵型相克指南         │ https://www.gamersky.com/handboo… │
│ 14   │ 热血三国- 皇陵探宝 - 4399网页游戏 │ https://web.4399.com/rxsg/yxzy/g… │
│ 15   │ 《热血三国3》新手综合攻略         │ https://www.gamersky.com/handboo… │
│      │ 新区霸服全指南-游民星空           │                                   │
│      │ GamerSky.com                      │                                   │
│ 16   │ 《热血三国3》最强新手指南         │ https://www.gamersky.com/handboo… │
│      │ 游戏玩法全解-游民星空             │                                   │
│      │ GamerSky.com                      │                                   │
│ 17   │ 4399热血三国之如何练就百级将      │ http://web.4399.com/rxsg/wjgl_27… │
│ 18   │ 热血三国远程兵攻击力怎么计算？有… │ https://www.ledu.com/sg/dyjh/ans… │
│ 19   │ 《热血三国》灵兽玉玺版本预告      │ https://sg.ledu.com/news/2024-09… │
│ 20   │ 《热血三国3》全神将属性数据及招 … │ https://www.gamersky.com/handboo… │
│      │ - 游民星空                        │                                   │
│ 21   │ 世界地图-网页游戏:91WAN.COM       │ http://www.91wan.com/rxsg/junshi… │
│ 22   │ 世界地图-网页游戏:91WAN.COM       │ http://www.91wan.com/rxsg/mcmjyo… │
│ 23   │ 4399热血三国新版1.1.09版本功能介… │ http://web.4399.com/rxsg/news_12… │
│ 24   │ 4399《热血三国》“决战天下v2.0”规… │ http://web.4399.com/rxsg/news_11… │
│ 25   │ 热血三国- 科技一览                │ https://web.4399.com/rxsg/yxzl/k… │
│ 26   │ 4399热血三国科技的详解            │ http://web.4399.com/rxsg/wjgl_16… │
│ 27   │ 建筑说明 - 热血三国               │ http://3kingdom.com.my/sg3/intro… │
│ 28   │ 建筑一览- 热血三国                │ http://web.4399.com/rxsg/yxzl_23… │
│ 29   │ 《热血三国3》竞技场指南迈向王者 … │ https://www.gamersky.com/handboo… │
│      │ - 游民星空                        │                                   │
│ 30   │ 热血三国决战天下规则              │ https://www.ledu.com/sg/wiki/wik… │
└──────┴───────────────────────────────────┴───────────────────────────────────┘

───────────────────────────── 30 sources | 389.99s ─────────────────────────────
