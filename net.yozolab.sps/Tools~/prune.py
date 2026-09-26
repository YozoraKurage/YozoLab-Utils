import re,os,sys,collections,json
ROOT=sys.argv[1]; APPLY=len(sys.argv)>2 and sys.argv[2]=='--apply'
files={}
for root,_,fs in os.walk(ROOT):
    for f in fs:
        if f.endswith('.cs'):
            p=os.path.relpath(os.path.join(root,f),ROOT); files[p]=open(os.path.join(root,f),encoding='utf-8').read()
decl=collections.defaultdict(set)
for p,s in files.items():
    for m in re.finditer(r'\b(?:class|struct|interface|enum)\s+([A-Z]\w*)',s): decl[m.group(1)].add(p)
names=set(decl)
tok=re.compile(r'\b[A-Z]\w*\b')
def strip(s): return re.sub(r'/\*.*?\*/','',re.sub(r'//[^\n]*','',s),flags=re.S)
refs={p:{t for t in set(tok.findall(strip(s))) if t in names} for p,s in files.items()}
# 起点
ROOTS_RE=[r'ShowInFirstPersonBuilder', r'FakeHeadService', r'^Runtime/Component/Sps', r'^Runtime/Component/SpsComponent', r'Haptic', r'/Sps', r'Sps[A-Z]\w*\.cs$', r'^Runtime/Model/StateAction/', r'^Editor-Avatars/Actions/', r'^Editor-Common/Injector/', r'TpsScaleFix', r'Ogb', r'OGB', r'_InternalsVisibleTo']
KEEP_SERVICES={'MenuService','ParamsService','ControllersService','AllClipsService','ActionClipService','ClipBuilderService','ClipFactoryService','DbtLayerService','FrameTimeService','SmoothingService','HapticContactsService','HapticAnimContactsService','InitBehavioursService','IsObjectEnabledService','LayerSourceService','OgbEnabledService','OverlappingContactsFixService','ParameterInjectService','ParameterSourceService','RestingStateService','ScalePropertyCompensationService','SpsOptionsService','SpsPlayerIdService','SpsSendersForAllService','WorldScaleDetectorService','AvatarBindingStateService','BakeHapticPlugsService','BakeHapticSocketsService','BakeHapticVersionsService','GlobalsService','FinalizeMenuService','MenuChangesService','ActionConflictResolverService','ExceptionService','ObjectMoveService','FindAnimatedTransformsService','OriginalAvatarService','ValidateBindingsService','FixAnimatedPhysbonesService','PhysboneResetService','ParticleSystemFixService','FakeHeadService','FullBodyEmoteService','AvatarColliderService','VrcsdkGlobalColliders','FakeHeadService'}
# サービス（Service フォルダ）のうち許可リスト外、フック、メニュー、ビルダー入口などは辿らない
def excluded(p):
    b=os.path.basename(p)[:-3]
    if '/Hooks/' in p or p.startswith('Editor-Common/Hooks') or p.startswith('Editor-Avatars/Hooks'): return True
    if p.startswith('Editor-Avatars/Menu/') and not re.search(r'Sps|Haptic|Dps',b): return True
    if p.startswith('Editor-Common/Menu/') and not re.search(r'Sps|Haptic',b): return True
    if '/Service/' in p and b not in KEEP_SERVICES: return True
    if b in ('VRCFuryBuilder','PlayModeTrigger','BadInstallDetector','VRCFPackageUtils','PreSaveVerifier','VRCFuryInjectorBuilder'): return True
    if p.startswith('Editor-Avatars/Feature/') and '/Base/' not in p and b not in ('SpsOptionsBuilder','SpsTouchReceiverBuilder','SpsTouchSenderBuilder','TpsScaleFixBuilder','ShowInFirstPersonBuilder'): return True
    if p.startswith('Runtime/Model/Feature/') and b not in ('FeatureModel','NewFeatureModel','LegacyFeatureModel','SpsOptions','TpsScaleFix','ShowInFirstPerson'): return True
    if p.startswith('Editor-Avatars/Updater') or p.startswith('Editor-Avatars/PlayMode'): return True
    return False
roots=[p for p in files if any(re.search(r,p) for r in ROOTS_RE) and not excluded(p)]
seen=set(roots); q=list(roots)
while q:
    p=q.pop()
    for t in refs[p]:
        for f in decl[t]:
            if f not in seen and not excluded(f): seen.add(f); q.append(f)
lines=lambda ps: sum(files[p].count('\n') for p in ps)
print('keep',len(seen),lines(seen),'drop',len(files)-len(seen),lines(set(files)-seen))
by=collections.Counter()
for p in seen: by['/'.join(p.split('/')[:2])]+=files[p].count('\n')
for k,v in by.most_common(30): print(f'{v:6d} {k}')
open(os.path.join(os.path.dirname(__file__),'keep.txt'),'w').write('\n'.join(sorted(seen)))
open(os.path.join(os.path.dirname(__file__),'drop.txt'),'w').write('\n'.join(sorted(set(files)-seen)))
if APPLY:
    for p in set(files)-seen:
        os.remove(os.path.join(ROOT,p)); 
        if os.path.exists(os.path.join(ROOT,p+'.meta')): os.remove(os.path.join(ROOT,p+'.meta'))
