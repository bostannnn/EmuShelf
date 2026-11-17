/* Big Type Screens: three options for every achievement of the running game. Loaded by proposals-screens.html after screens-engine.js.
   What today's badge grid holds, all of it kept in each option: the count and the points, All / Locked / Unlocked with their counts
   on LB and RB, every badge (locked ones dimmed, a hardcore unlock edged in gold, a hidden one that A reveals), the focused one's
   name, description, points and date, Y to refresh, B to close, and the line that says what a refresh is doing. */
(()=>{
const B=window.BTS;if(!B)return;const {el,esc,roll,letters,rise,pulse,RM,SPRING,EASEIN}=B;
const COL='#6B6F7E',GAME='Final Fantasy VII';
const ACH=[['Midgar Escape','Leave Midgar for the first time',5,'2 Nov'],['Chocobo Breeder','Breed a gold chocobo',25,'3 Nov'],['Limit Break','Learn a level 4 limit break',10,'4 Nov'],
 ['Golden Saucer','Win the Speed Square jackpot',10,'5 Nov'],['Materia Hunter','Master any materia',10,'6 Nov'],['Knights of the Round','Obtain the Knights of the Round',25,null],
 ['Ruby Weapon','Defeat Ruby Weapon',50,null],['Emerald Weapon','Defeat Emerald Weapon',50,null],['Snowboarder','Finish the snowboard course',5,null],['Fort Condor','Win every Fort Condor battle',10,null],
 ['Gil Collector','Hold 1,000,000 gil',10,null],['The Planet’s Cry','Reach the Northern Crater',25,null]].map(([t,d,p,e],i)=>({t,d,p,e,hidden:i===11,hc:i===1}));
const N=ACH.length,BC=['#D9573B','#3B8ED9','#4CB36B','#B34CA8','#E0B03A','#3BB2D9'];
const SHAPE=['left:27%;top:27%;width:46%;height:46%;border-radius:50%;background:rgba(255,255,255,.85)','left:29%;top:29%;width:42%;height:42%;transform:rotate(45deg);background:rgba(255,255,255,.85)',
 'left:22%;top:22%;width:56%;height:56%;border-radius:50%;box-sizing:border-box;border:.5em solid rgba(255,255,255,.85)','left:14%;top:39%;width:72%;height:22%;border-radius:1em;transform:rotate(-24deg);background:rgba(255,255,255,.85)'];
const bdg=(i,sz)=>`<span class="bdg" style="width:${sz}px;height:${sz}px;font-size:${sz/8}px;--a:${BC[i%6]}"><i style="${SHAPE[i%4]}"></i></span>`;
const STEPS=[
 {label:'The Achievements tab',ms:2400},
 {label:'A opens every badge',open:1,f:0,filter:0,key:'A',ms:2800},
 {label:'Moving to a locked one',open:1,f:6,filter:0,key:'R',ms:2600},
 {label:'RB shows only the locked',open:1,f:6,filter:1,key:'RB',ms:2800},
 {label:'A hidden one',open:1,f:11,filter:1,key:'D',ms:2600},
 {label:'A reveals it',open:1,f:11,filter:1,rev:1,key:'A',ms:2800},
 {label:'Y refreshes',open:1,f:5,filter:0,rev:1,fresh:1,key:'Y',ms:4200},
 {label:'B closes',fresh:1,key:'B',ms:2600}];
// everything an option needs to draw one moment
function model(st,fresh){const got=i=>!!ACH[i].e||(fresh&&i===5),all=ACH.map((_,i)=>i),lk=all.filter(i=>!got(i)),un=all.filter(got),a=ACH[st.f],veiled=a.hidden&&!st.rev&&!got(st.f);
 return {f:st.f,filter:st.filter,key:st.key,got,veil:i=>ACH[i].hidden&&!st.rev&&!got(i),list:[all,lk,un][st.filter],counts:[N,lk.length,un.length],n:un.length,pts:un.reduce((s,i)=>s+ACH[i].p,0),
  title:veiled?'Hidden achievement':a.t,desc:veiled?'Reveal to see this achievement’s details.':a.d,meta:`${a.p} pts`+(got(st.f)?`  ·  Earned ${a.e||'12 Nov'}`:''),veiled};}
const dress=(b,i,m)=>{b.classList.toggle('lk',!m.got(i)&&!m.veil(i));b.classList.toggle('hid',m.veil(i));b.classList.toggle('hc',ACH[i].hc);};
const FILT=pos=>`<div class="tabs filt" style="${pos}"><i class="shk">LB</i><span>All<b></b></span><span>Locked<b></b></span><span>Unlocked<b></b></span><i class="shk">RB</i></div>`;
const setFilt=(L,m)=>[...L.querySelectorAll('.filt>span')].forEach((s,i)=>{s.classList.toggle('on',i===m.filter);roll(s.querySelector('b'),String(m.counts[i]));});
const dirOf=k=>({R:'R',L:'L',D:'U',U:'D'})[k]||'U';

function option(make){return D=>{
 B.gameOnTop(D,GAME,COL,'DuckStation');
 const C=B.companion(D,{title:GAME,col:COL,tab:1,ach:[5,N],recent:[['Materia Hunter','6 Nov 2026'],['Golden Saucer','5 Nov 2026'],['Limit Break','4 Nov 2026']],dock:B.DOCK5});
 const L=D.layer('badges');L.hidden=true;const O=make(D,L);let tok=0;
 return {states:STEPS,refit(){C.fit();},
  show(i,from){const st=STEPS[i],pv=from>=0?STEPS[from]:{},t=++tok;
   if(st.key)D.pressHint(st.key);
   if(!st.open){if(pv.open){O.close();setTimeout(()=>{if(t===tok)L.hidden=true;},300);}C.show(!!pv.open);C.setAch(st.fresh?6:5);D.ringOff();D.hintsBot([]);return;}
   if(!pv.open){L.hidden=false;L.classList.remove('gone');C.hide();D.ringOff();O.open();}
   const late=!!st.fresh&&from===i-1&&!RM,m=model(st,!!st.fresh&&!late);
   O.draw(m,pv.open?pv:{},late?'Loading achievement details…':'');
   D.hintsBot([...(m.veiled?[['A','Reveal']]:[]),['Y','Refresh'],['B','Close']]);
   // the refresh comes back with one more unlocked: the badge takes its colour and the figures roll
   if(late)setTimeout(()=>{if(t===tok)O.draw(model(st,true),st,'');},1500);}};};}

/* 4 · Wall: every badge on the colour, the focused one named in big type under them */
function wall(D,L){
 L.innerHTML='<div class="pk0" style="left:40px;top:18px"></div><div class="bigh disp fig" style="left:38px;top:36px;font-size:50px"></div>'+FILT('right:40px;top:58px')+
  '<div class="clip" style="left:0;top:98px;width:620px;height:252px"><div class="scr2"></div></div><div class="bigh disp nm" style="left:38px;top:350px"></div><div class="aline ds" style="left:40px;top:410px"></div><div class="aline dim mt" style="left:40px;top:438px"></div>';
 const q=s=>L.querySelector(s),sc=q('.scr2');let r0=0;
 const tiles=ACH.map((a,i)=>{const d=el('div','btile',bdg(i,64));sc.appendChild(d);return d;});
 return {
  open(){q('.nm')._t=null;if(!RM)tiles.forEach((e,i)=>e.animate([{scale:'.3',opacity:0},{scale:'1',opacity:1}],{duration:440,delay:(i%7+(i/7|0))*38,easing:SPRING,fill:'backwards'}));},
  close(){D.ringOff();if(!RM)tiles.forEach((e,i)=>e.animate([{},{translate:'0 40px',opacity:0}],{duration:180,delay:(i%7)*12,easing:EASEIN}));L.classList.add('gone');},
  draw(m,pv,status){roll(q('.pk0'),status||`Achievements  ·  ${m.pts} pts`);roll(q('.fig'),`${m.n} / ${N}`);setFilt(L,m);
   const fp=m.list.indexOf(m.f),fr=fp/7|0;if(fr<r0)r0=fr;if(fr>r0+2)r0=fr-2;if(m.list.length<=21)r0=0;sc.style.transform=`translateY(${-r0*80}px)`;
   tiles.forEach((e,i)=>{const p=m.list.indexOf(i),b=e.firstChild,was=b.classList.contains('lk');dress(b,i,m);e.classList.toggle('out',p<0);e.classList.toggle('f',i===m.f);
    if(p>=0){e.style.left=37+(p%7)*80+'px';e.style.top=14+(p/7|0)*80+'px';}if(was&&m.got(i))pulse(e,.6);});
   letters(q('.nm'),m.title,{base:40,max:540,dir:pv.open?dirOf(m.key):'U'});roll(q('.ds'),m.desc);roll(q('.mt'),m.meta);
   D.ringTo({left:37+(fp%7)*80-10,top:98+14+(fr-r0)*80-10,width:84,height:84},{radius:20,pop:!pv.open});}};}

/* 5 · Roll call: one row of type for each, and the focused row opens to say what it asks */
function rollcall(D,L){
 L.innerHTML='<div class="bigh disp hd" style="left:40px;top:22px"></div><div class="cnt fig" style="right:40px;top:22px"></div><div class="aline ln" style="left:44px;top:86px"></div>'+FILT('right:40px;top:86px')+
  '<div class="clip" style="left:0;top:110px;width:620px;height:362px"><div class="scr2"></div></div>';
 const q=s=>L.querySelector(s),sc=q('.scr2');let s0=0;
 const rows=ACH.map((a,i)=>{const d=el('div','row brow');d.style.cssText='left:40px;width:540px;height:38px';d.innerHTML=`<span class="rl">${bdg(i,30)}<span class="nm"></span></span><span class="rv"></span><div class="rd"></div>`;sc.appendChild(d);return d;});
 return {
  open(){q('.hd')._t=null;letters(q('.hd'),'Achievements',{base:50,max:380});rise(rows.slice(0,8),{dx:70,dy:0,stagger:26,ms:420});},
  close(){D.ringOff();if(!RM)rows.forEach((e,i)=>e.animate([{},{translate:'0 40px',opacity:0}],{duration:170,delay:Math.min(i,8)*10,easing:EASEIN}));L.classList.add('gone');},
  draw(m,pv,status){letters(q('.hd'),'Achievements',{base:50,max:380});roll(q('.fig'),`${m.n} / ${N}`);roll(q('.ln'),status||`${m.pts} pts`);setFilt(L,m);
   const fp=m.list.indexOf(m.f);if(fp<s0)s0=fp;if(fp>s0+6)s0=fp-6;if(m.list.length<=7)s0=0;sc.style.transform=`translateY(${-s0*46}px)`;
   rows.forEach((e,i)=>{const p=m.list.indexOf(i),a=ACH[i],b=e.querySelector('.bdg'),was=b.classList.contains('lk'),v=m.veil(i);dress(b,i,m);
    e.classList.toggle('out',p<0);e.classList.toggle('f',i===m.f);e.classList.toggle('locked',!m.got(i));
    if(p>=0)e.style.top=8+p*46+(p>fp?30:0)+'px';
    roll(e.querySelector('.nm'),v?'Hidden achievement':a.t);roll(e.querySelector('.rv'),`${a.p} pts`);
    e.querySelector('.rd').textContent=v?'Reveal to see this achievement’s details.':a.d+(m.got(i)?`  ·  Earned ${a.e||'12 Nov'}`:'');if(was&&m.got(i))pulse(b,.5);});
   D.ringTo({left:26,top:110+8+(fp-s0)*46-6,width:568,height:80},{radius:16,pop:!pv.open});}};}

/* 6 · One at a time: the focused badge large, its name in big type, the rest queueing beside it */
function spotlight(D,L){
 L.innerHTML='<div class="pk0" style="left:40px;top:22px"></div>'+FILT('right:40px;top:54px')+'<div class="reel" style="left:0;top:60px;width:620px;height:200px"></div><div class="bigh disp nm" style="left:38px;top:258px"></div>'+
  '<div class="aline ds" style="left:40px;top:344px"></div><div class="aline dim mt" style="left:40px;top:372px"></div><div class="aline dim of" style="right:40px;top:404px"></div><div class="track" style="left:40px;top:432px;width:540px;height:22px"></div>';
 const q=s=>L.querySelector(s),box=q('.reel'),tr=q('.track');
 const items=ACH.map((a,i)=>{const d=el('div','sitem',bdg(i,150));box.appendChild(d);return d;}),ticks=ACH.map(()=>{const t=el('i');tr.appendChild(t);return t;});
 const XS=[0,174,272,346,404,450],SC=[1,.56,.4,.3,.22,.16],OP=[1,.95,.75,.5,.28,0];
 return {
  open(){q('.nm')._t=null;if(!RM)items.forEach((e,i)=>{if(i<6)e.animate([{translate:'0 260px',opacity:0},{translate:'0 0'}],{duration:520,delay:i*50,easing:SPRING,fill:'backwards'});});rise(ticks,{dy:14,stagger:16,ms:360});},
  close(){D.ringOff();if(!RM)items.forEach(e=>e.animate([{},{translate:'0 200px',opacity:0}],{duration:200,easing:EASEIN}));L.classList.add('gone');},
  draw(m,pv,status){roll(q('.pk0'),status||`Achievements  ·  ${m.n} / ${N}  ·  ${m.pts} pts`);setFilt(L,m);const fp=m.list.indexOf(m.f);
   items.forEach((e,i)=>{const p=m.list.indexOf(i),b=e.firstChild,was=b.classList.contains('lk');dress(b,i,m);const d=p-fp,k=Math.min(Math.abs(d),5);
    if(p<0){e.style.opacity=0;e.style.transform='translateX(300px) scale(.1)';}
    else if(d<0){e.style.opacity=0;e.style.transform=`translateX(${-120-40*k}px) scale(.6)`;}
    else{e.style.opacity=OP[k];e.style.transform=`translateX(${XS[k]}px) scale(${SC[k]})`;}
    if(was&&m.got(i))pulse(e,.7);});
   ticks.forEach((t,i)=>{t.classList.toggle('on',m.got(i));t.classList.toggle('cur',i===m.f);t.classList.toggle('off',m.list.indexOf(i)<0);});
   letters(q('.nm'),m.title,{base:64,max:540,dir:pv.open?(dirOf(m.key)==='L'?'L':'R'):'U'});roll(q('.ds'),m.desc);roll(q('.mt'),m.meta);roll(q('.of'),`${fp+1} of ${m.list.length}`);
   D.ringTo({left:28,top:80,width:174,height:174},{radius:32,pop:!pv.open});if(pv.open)pulse(D.ring,.94);}};}

B.add('badges',[
 {name:'Wall',line:'Every badge sits on the colour, and the focused one is named in big type underneath.',build:option(wall)},
 {name:'Roll call',line:'One row of type for each achievement; the focused row opens to say what it asks.',build:option(rollcall)},
 {name:'One at a time',line:'The focused badge is large with its name in big type, and the rest queue beside it.',build:option(spotlight)}]);
})();
