/* Big Type Screens: three options for All apps. Loaded by proposals-screens.html after screens-engine.js.
   What today's drawer holds, all of it kept in each option: the title (All apps, or Choose an app for slot N), the apps A to Z with
   their icons and names, the line after a pin, A to open (or pin here), Y to pin (or clear the slot), B to close. */
(()=>{
const B=window.BTS;if(!B)return;const {el,esc,roll,letters,rise,pulse,RM,SPRING,EASEIN,deep}=B;
const COL='#2B5BC7',GAME='Okami';
const APPS=[['Azahar','A','#B0477E'],['Browser','B','#2F80ED'],['Calculator','=','#465068'],['Calendar','31','#D2434B'],['Camera','Ca','#2B2D3A'],['Clock','Cl','#1F2333'],
 ['Discord','D','#5865F2'],['Dolphin','Do','#2F74B5'],['DuckStation','Du','#C7962A'],['Files','F','#D9912B'],['Gallery','G','#16A974'],['melonDS','m','#6B6FA8'],
 ['Music','M','#E5484D'],['NetherSX2','N','#2456D6'],['PPSSPP','P','#E08A14'],['Settings','Se','#666C7C'],['Steam Link','S','#1B2233'],['YouTube','Y','#D93025']].map(([n,l,c])=>({n,l,c}));
const ic=(a,sz)=>`<span class="ic" style="width:${sz}px;height:${sz}px;background:${a.c};font-size:${Math.round(sz*.4)}px">${a.l}</span>`;
const N=APPS.length,PINNED=[1,12,17,9],DISCORD=6;   // Browser, Music, YouTube, Files; the fifth slot is free
const STEPS=[
 {label:'In a game, on the dock',ms:2300},
 {label:'A opens All apps',open:1,f:0,key:'A',ms:2700},
 {label:'Moving',open:1,f:5,key:'D',ms:1700},
 {label:'Moving',open:1,f:DISCORD,key:'R',ms:2300},
 {label:'Y pins it to the dock',open:1,f:DISCORD,pin:1,key:'Y',ms:3000},
 {label:'Changing a dock slot',open:1,f:12,pick:1,pin:1,ms:3200},
 {label:'B closes',pin:1,key:'B',ms:2600},
 {label:'Opened from home',open:1,home:1,f:DISCORD,pin:1,ms:3600}];
const slotOf=(i,st)=>{const p=PINNED.indexOf(i);return p>=0?p+1:(st.pin&&i===DISCORD?5:0);};
const title=st=>st.pick?'Choose an app for slot 2':'All apps';
const notice=st=>st.pin&&!st.pick&&st.f===DISCORD?'Discord is in the dock, slot 5':'';
const where=(i,st)=>{const s=slotOf(i,st);return s?`In the dock, slot ${s}`:'Not in the dock';};
const dirOf=k=>({R:'R',L:'L',D:'U',U:'D'})[k]||'U';

function option(make){return D=>{
 const T=B.stage(D);T.backdrop('playstation2','ps2-orbs-v3',COL);
 const over=B.gameOnTop(D,GAME,COL,'NetherSX2');
 const C=B.companion(D,{title:GAME,col:COL,tab:0,time:'24:10',total:'22 h 15 min',ach:[9,24],dock:[...PINNED.map(i=>ic(APPS[i],44)),null]});
 const L=D.layer('apps');L.hidden=true;const O=make(D,L);
 const slot5=C.el.querySelector('[data-dock="5"]');let tok=0,wasPin=false;
 return {states:STEPS,refit(){C.fit();T.refit();},
  show(i,from){const st=STEPS[i],pv=from>=0?STEPS[from]:{},home=!!st.home,a=APPS[st.f||0];
   over.style.opacity=home?0:1;T.hide=!home;D.flood(home?COL:deep(COL),310,500,from<0);
   if(st.key)D.pressHint(st.key);
   // the dock's fifth slot takes the pinned app
   if(!!st.pin!==wasPin){wasPin=!!st.pin;slot5.classList.toggle('free',!wasPin);slot5.innerHTML=wasPin?ic(APPS[DISCORD],44):'';if(wasPin)pulse(slot5,.3);}
   if(!st.open){
    if(pv.open){const t=++tok;O.close();setTimeout(()=>{if(t===tok)L.hidden=true;},300);}
    C.el.classList.remove('peek');C.show(!!pv.open);D.hintsBot([]);
    D.ringTo(C.dockRect(0),{radius:19,pop:!!pv.open||from<0});return;}
   if(!pv.open||!!pv.home!==home){tok++;L.hidden=false;
    if(home)C.el.classList.add('gone');else if(O.under==='peek'){C.el.classList.remove('gone');C.el.classList.add('peek');}else C.hide();
    D.ringOff();O.open(st);}
   O.focus(st,pv.open&&!!pv.home===home?pv:{});
   D.hintsBot(home?[]:st.pick?[['A','Pin here'],['Y','Clear slot'],['B','Close']]:[['A','Open'],['Y','Pin to dock'],['B','Close']],O.hintsY||24);
   if(home)T.set({kicker:title(st),word:a.n,meta:notice(st)||where(st.f,st),meta2:`${st.f+1} of ${N}  ·  everything on this device`,hints:[['A','Open'],['Y','Pin to dock'],['B','Close']]},pv.home?dirOf(st.key):'open');}};};}

/* 1 · Names: rows of plain type under the ring, as the menus and Settings are drawn */
function names(D,L){
 L.innerHTML='<div class="bigh disp" style="left:40px;top:22px"></div><div class="cnt" style="right:40px;top:22px"></div><div class="aline" style="left:44px;top:84px"></div><div class="clip" style="left:0;top:104px;width:596px;height:380px"><div class="scr2"></div></div>';
 const [head,cnt,line]=L.children,sc=L.querySelector('.scr2');let c0=0;
 const rows=APPS.map((a,i)=>{const c=i/8|0,r=i%8,d=el('div','row');d.style.cssText=`left:${40+c*284}px;top:${8+r*46}px;width:256px;height:38px`;
  d.innerHTML=`<span class="rl">${ic(a,26)}${esc(a.n)}</span><span class="rv"></span>`;d._c=c;d._r=r;sc.appendChild(d);return d;});
 return {
  open(st){head._t=null;L.classList.remove('gone');rise(rows.filter(r=>r._c-c0<2&&r._c>=c0),{dx:70,dy:0,stagger:16,ms:420});},
  close(){D.ringOff();if(!RM)rows.forEach((e,i)=>e.animate([{},{transform:'translateY(40px)',opacity:0}],{duration:170,delay:Math.min(i,8)*10,easing:EASEIN}));L.classList.add('gone');},
  focus(st,pv){letters(head,title(st),{base:50,max:410});roll(cnt,String(N));roll(line,notice(st)||(st.pick?'Y clears the slot instead':'Everything on this device'));
   rows.forEach((r,i)=>{r.classList.toggle('f',i===st.f);const s=slotOf(i,st);roll(r.querySelector('.rv'),s?`Dock ${s}`:'');});
   const r=rows[st.f];if(r._c<c0)c0=r._c;if(r._c>c0+1)c0=r._c-1;sc.style.transform=`translateX(${-c0*284}px)`;
   D.ringTo({left:40+(r._c-c0)*284-14,top:112+r._r*46-6,width:284,height:50},{radius:16,pop:!pv.open});}};}

/* 2 · Shelf: the icons on the colour in a grid, as home is; the focused app's name is the big name */
function shelf(D,L){
 L.innerHTML='<div class="pk0" style="left:54px;top:16px"></div><div class="bigh disp" style="left:52px;top:34px"></div><div class="cnt sm" style="right:54px;top:44px"></div><div class="clip" style="left:0;top:86px;width:620px;height:408px"><div class="scr2"></div></div>';
 const [kick,name,cnt]=L.children,sc=L.querySelector('.scr2');let r0=0;
 const tiles=APPS.map((a,i)=>{const c=i%5,r=i/5|0,d=el('div','atile',ic(a,80));d.style.cssText=`left:${58+c*106}px;top:${14+r*98}px`;d._c=c;d._r=r;sc.appendChild(d);return d;});
 return {
  open(st){name._t=null;L.classList.remove('gone');const f=tiles[st.f];
   if(!RM)tiles.forEach(e=>e.animate([{transform:'scale(.45)',opacity:0},{opacity:1}],{duration:440,delay:Math.min(Math.hypot(e._c-f._c,e._r-f._r),6)*34,easing:SPRING,fill:'backwards'}));},
  close(){D.ringOff();const f=tiles.find(e=>e.classList.contains('f'))||tiles[0];
   if(!RM)tiles.forEach(e=>{const dx=e._c-f._c,dy=e._r-f._r;e.animate([{opacity:1},{transform:`translate(${dx*26}px,${dy*26}px) scale(.55)`,opacity:0}],{duration:240,delay:Math.min(Math.hypot(dx,dy),6)*18,easing:EASEIN});});L.classList.add('gone');},
  focus(st,pv){const a=APPS[st.f],f=tiles[st.f];roll(kick,notice(st)||title(st));letters(name,a.n,{base:46,max:380,dir:pv.open?dirOf(st.key):'U'});roll(cnt,`${st.f+1} / ${N}`);
   if(f._r<r0)r0=f._r;if(f._r>r0+3)r0=f._r-3;sc.style.transform=`translateY(${-r0*98}px)`;
   tiles.forEach(e=>{e.classList.toggle('f',e===f);const dx=e._c-f._c,dy=e._r-f._r,near=e!==f&&Math.abs(dx)<=1&&Math.abs(dy)<=1;e.style.setProperty('--px',(near?dx*4:0)+'px');e.style.setProperty('--py',(near?dy*4:0)+'px');});
   D.ringTo({left:58+f._c*106-12,top:86+14+(f._r-r0)*98-12,width:104,height:104},{radius:28,pop:!pv.open});}};}

/* 3 · Reel: one row of icons through the ring, the focused app's name as large as it will go */
function reel(D,L){
 L.innerHTML='<div class="pk0" style="left:40px;top:30px"></div><div class="cnt sm" style="right:40px;top:24px"></div><div class="reel" style="left:0;top:0;width:620px;height:300px"></div><div class="bigh disp" style="left:36px;top:296px"></div><div class="aline" style="left:40px;top:410px"></div>';
 const [kick,cnt,box,name,line]=L.children;
 const items=APPS.map(a=>{const d=el('div','ritem',ic(a,132));box.appendChild(d);return d;});
 const XS=[0,150,250,326,386],SC=[1,.6,.42,.3,.2],OP=[1,.95,.7,.38,0];
 const place=f=>items.forEach((e,i)=>{const d=i-f,k=Math.min(Math.abs(d),4);e.style.transform=`translateX(${XS[k]*Math.sign(d)}px) scale(${SC[k]})`;e.style.opacity=OP[k];e.style.zIndex=5-k;});
 return {under:'peek',hintsY:74,
  open(st){name._t=null;L.classList.remove('gone');place(st.f);if(!RM)items.forEach((e,i)=>{const k=Math.abs(i-st.f);if(k<5)e.animate([{translate:'0 300px',opacity:0},{translate:'0 0'}],{duration:520,delay:k*45,easing:SPRING,fill:'backwards'});});},
  close(){D.ringOff();if(!RM)items.forEach(e=>e.animate([{},{translate:'0 300px',opacity:0}],{duration:220,easing:EASEIN}));L.classList.add('gone');},
  focus(st,pv){const a=APPS[st.f];place(st.f);roll(kick,title(st));roll(cnt,`${st.f+1} / ${N}`);
   letters(name,a.n,{base:96,max:548,dir:pv.open?dirOf(st.key)==='L'?'L':'R':'U'});roll(line,notice(st)||where(st.f,st));
   D.ringTo({left:229,top:130,width:162,height:162},{radius:44,pop:!pv.open});if(pv.open)pulse(D.ring,.94);}};}

B.add('apps',[
 {name:'Names',line:'A list of plain names under the ring, drawn like the menus and Settings.',build:option(names)},
 {name:'Shelf',line:'The icons sit on the colour in a grid like home, and the focused app’s name is the big name.',build:option(shelf)},
 {name:'Reel',line:'One row of icons runs through the ring, with the focused app’s name as large as it will go.',build:option(reel)}]);
})();
