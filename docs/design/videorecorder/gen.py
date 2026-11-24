# -*- coding: utf-8 -*-
import json, os
BG="/_blob/af80c85f1515fda0a5deb874e280c472"
ACC="@ACC@"            # -> {{accent}}
OUT="/home/claude/design/root/project"

# ---------- palette
TEXT="#efe9e7"; DIM="#a39896"; FAINT="#8c817f"
SURF="rgba(255,255,255,0.045)"; SURF2="rgba(255,255,255,0.07)"; LINE="rgba(255,255,255,0.08)"
GLASS="background: rgba(18,14,14,0.86); backdrop-filter: blur(18px); -webkit-backdrop-filter: blur(18px); border: 1px solid rgba(255,255,255,0.08); box-shadow: 0 24px 60px rgba(0,0,0,0.45)"
REC="#d41f3c"; ROSE="#f0a6b1"
TAKECOL=["#ff5a6e","#f2a0ad","#d9b48c","#b8aeac","#b27ae0"]

P={
"play":'<path d="M7 4.5v15l12.5-7.5z" fill="currentColor" stroke="none"></path>',
"pause":'<rect x="6" y="4.5" width="4" height="15" rx="1" fill="currentColor" stroke="none"></rect><rect x="14" y="4.5" width="4" height="15" rx="1" fill="currentColor" stroke="none"></rect>',
"stop":'<rect x="5.5" y="5.5" width="13" height="13" rx="2.5" fill="currentColor" stroke="none"></rect>',
"back":'<path d="M19 20L9 12l10-8z" fill="currentColor" stroke="none"></path><path d="M5 19V5"></path>',
"edit":'<path d="M12 20h9"></path><path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z"></path>',
"rerec":'<path d="M21 12a9 9 0 1 1-2.6-6.4L21 8"></path><path d="M21 3v5h-5"></path>',
"more":'<circle cx="5" cy="12" r="1.3" fill="currentColor"></circle><circle cx="12" cy="12" r="1.3" fill="currentColor"></circle><circle cx="19" cy="12" r="1.3" fill="currentColor"></circle>',
"x":'<path d="M18 6L6 18M6 6l12 12"></path>',
"undo":'<path d="M3 7v6h6"></path><path d="M21 17a9 9 0 0 0-15-6.7L3 13"></path>',
"plus":'<path d="M12 5v14M5 12h14"></path>',
"minus":'<path d="M5 12h14"></path>',
"down":'<path d="M6 9l6 6 6-6"></path>',
"left":'<path d="M15 18l-6-6 6-6"></path>',
"layers":'<path d="M12 2l10 5-10 5L2 7z"></path><path d="M2 17l10 5 10-5"></path><path d="M2 12l10 5 10-5"></path>',
"video":'<path d="M23 7l-7 5 7 5z"></path><rect x="1" y="5" width="15" height="14" rx="2"></rect>',
"flame":'<path d="M8.5 14.5A2.5 2.5 0 0 0 11 12c0-1.4-.5-2-1-3-1.1-2.1-.2-4 2-6 .5 2.5 2 4.9 4 6.5 2 1.6 3 3.5 3 5.5a7 7 0 1 1-14 0c0-1.2.4-2.3 1-3.3.2 1.4 1.2 2.3 2.5 2.8z"></path>',
"clap":'<path d="M4 11h16v9a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1z"></path><path d="M4 11l-.6-3.2a1 1 0 0 1 .8-1.2L18 4l.6 3.2z"></path><path d="M8.5 5.6l2 3.6M13.6 4.7l2 3.6"></path>',
"sliders":'<path d="M4 21v-7M4 10V3M12 21v-9M12 8V3M20 21v-5M20 12V3M1 14h6M9 8h6M17 16h6"></path>',
"eye":'<path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8S1 12 1 12z"></path><circle cx="12" cy="12" r="3"></circle>',
"clock":'<circle cx="12" cy="12" r="9"></circle><path d="M12 7v5l3 2"></path>',
"flag":'<path d="M4 22V4"></path><path d="M4 4h13l-2 4 2 4H4"></path>',
"route":'<circle cx="6" cy="19" r="2"></circle><circle cx="18" cy="5" r="2"></circle><path d="M6 17V10a4 4 0 0 1 4-4h6"></path><path d="M18 7v7a4 4 0 0 1-4 4H8"></path>',
"user":'<path d="M20 21a8 8 0 0 0-16 0"></path><circle cx="12" cy="8" r="4"></circle>',
"nav":'<path d="M3 11l19-9-9 19-2-8z"></path>',
"orbit":'<circle cx="12" cy="12" r="3"></circle><path d="M20.5 9A9 9 0 1 0 21 13"></path><path d="M21 4v5h-5"></path>',
"tripod":'<rect x="8" y="2" width="8" height="6" rx="1.5"></rect><path d="M12 8v4M12 12l-6 10M12 12l6 10M12 12v10"></path>',
"copy":'<rect x="9" y="9" width="12" height="12" rx="2"></rect><path d="M5 15H4a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1h10a1 1 0 0 1 1 1v1"></path>',
"import":'<path d="M12 3v12"></path><path d="M7 10l5 5 5-5"></path><path d="M5 21h14"></path>',
"sun":'<circle cx="12" cy="12" r="4"></circle><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"></path>',
"cloud":'<path d="M17.5 19a4.5 4.5 0 1 0-1.4-8.8A7 7 0 1 0 6 18.9z"></path>',
"fog":'<path d="M4 7h16M3 11h18M5 15h14M7 19h10"></path>',
"rain":'<path d="M17.5 15a4.5 4.5 0 1 0-1.4-8.8A7 7 0 1 0 6 14.9"></path><path d="M8 17l-1 3M12 17l-1 3M16 17l-1 3"></path>',
"bolt":'<path d="M13 2L3 14h9l-1 8 10-12h-9z"></path>',
"mic":'<rect x="9" y="2" width="6" height="12" rx="3"></rect><path d="M5 11a7 7 0 0 0 14 0M12 18v4"></path>',
"loop":'<path d="M17 2l4 4-4 4"></path><path d="M3 11V9a3 3 0 0 1 3-3h15"></path><path d="M7 22l-4-4 4-4"></path><path d="M21 13v2a3 3 0 0 1-3 3H3"></path>',
"shield":'<path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"></path>',
"skull":'<path d="M12 3a8 8 0 0 0-5 14.2V20a1 1 0 0 0 1 1h8a1 1 0 0 0 1-1v-2.8A8 8 0 0 0 12 3z"></path><circle cx="9" cy="12" r="1.3" fill="currentColor"></circle><circle cx="15" cy="12" r="1.3" fill="currentColor"></circle>',
"scissors":'<circle cx="6" cy="6" r="3"></circle><circle cx="6" cy="18" r="3"></circle><path d="M20 4L8.1 15.9M14.5 14.5L20 20M8.1 8.1L12 12"></path>',
"move":'<path d="M5 9l-3 3 3 3M9 5l3-3 3 3M15 19l-3 3-3-3M19 9l3 3-3 3M2 12h20M12 2v20"></path>',
"rotate":'<path d="M21 12a9 9 0 1 1-2.6-6.4L21 8"></path><path d="M21 3v5h-5"></path>',
"mirror":'<path d="M12 3v18"></path><path d="M8 7L3 12l5 5z"></path><path d="M16 7l5 5-5 5z"></path>',
"pin":'<path d="M12 21s-7-6.2-7-11a7 7 0 1 1 14 0c0 4.8-7 11-7 11z"></path><circle cx="12" cy="10" r="2.5"></circle>',
"car":'<path d="M5 17h14v-5l-2-5H7l-2 5z"></path><path d="M7 17v2M17 17v2M5 12h14"></path>',
"check":'<path d="M20 6L9 17l-5-5"></path>',
"crosshair":'<circle cx="12" cy="12" r="9"></circle><path d="M22 12h-4M6 12H2M12 6V2M12 22v-4"></path>',
"key":'<rect x="2" y="6" width="20" height="12" rx="2"></rect><path d="M6 10h.01M10 10h.01M14 10h.01M18 10h.01M7 14h10"></path>',
"tag":'<path d="M20.6 13.4l-7.2 7.2a2 2 0 0 1-2.8 0L2 12V2h10l8.6 8.6a2 2 0 0 1 0 2.8z"></path><circle cx="7" cy="7" r="1.5"></circle>',
"smoke":'<path d="M3 12c2-2 4-2 6 0s4 2 6 0 4-2 6 0"></path><path d="M3 17c2-2 4-2 6 0s4 2 6 0 4-2 6 0"></path><path d="M3 7c2-2 4-2 6 0s4 2 6 0 4-2 6 0"></path>',
"flare":'<circle cx="12" cy="12" r="3"></circle><path d="M12 2v4M12 18v4M4.9 4.9l2.8 2.8M16.3 16.3l2.8 2.8M2 12h4M18 12h4M4.9 19.1l2.8-2.8M16.3 7.7l2.8-2.8"></path>',
"boom":'<path d="M12 2l2 6 6-3-3 6 6 2-6 2 3 6-6-3-2 6-2-6-6 3 3-6-6-2 6-2-3-6 6 3z"></path>',
"tank":'<rect x="2" y="12" width="20" height="7" rx="3.5"></rect><path d="M6 12V9h9v3M15 10h6"></path>',
"gas":'<path d="M12 2c3 4 6 7 6 11a6 6 0 0 1-12 0c0-4 3-7 6-11z"></path><path d="M12 13a2 2 0 0 0 2 2"></path>',
"signal":'<path d="M12 21v-7"></path><path d="M8 7c0-2 1.8-3 4-3s4 1 4 3-2 2.5-4 4c-2-1.5-4-2-4-4z"></path>',
"bag":'<path d="M6 8h12l1 13H5z"></path><path d="M9 8V6a3 3 0 0 1 6 0v2"></path>',
"shirt":'<path d="M8 3l-5 3 2 5 3-1v11h8V10l3 1 2-5-5-3a4 4 0 0 1-8 0z"></path>',
"rifle":'<path d="M2 13h14l2-2h4v3h-4l-2 2H9l-1 4H5l1-4H2z"></path>',
"helmet":'<path d="M4 15a8 8 0 0 1 16 0v2H4z"></path><path d="M9 12h6"></path>',
"med":'<rect x="3" y="6" width="18" height="14" rx="2"></rect><path d="M12 10v6M9 13h6M9 6V4h6v2"></path>',
"ammo":'<path d="M7 9a2 2 0 0 1 4 0v11H7z"></path><path d="M13 9a2 2 0 0 1 4 0v11h-4z"></path>',
"pick":'<path d="M4 6c5-3 11-3 16 0"></path><path d="M12 4v17"></path>',
"dot":'<circle cx="12" cy="12" r="6" fill="currentColor" stroke="none"></circle>',
}
def ic(n,s=16,c="currentColor",sw=1.9):
    return f'<svg width="{s}" height="{s}" viewBox="0 0 24 24" fill="none" stroke="{c}" stroke-width="{sw}" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" style="flex: none">{P[n]}</svg>'

FONT="font-family: 'Roboto Condensed', 'Roboto', system-ui, sans-serif"

def iconbtn(n,label,size=34,bg=SURF,color=TEXT,radius=10,extra=""):
    return f'<button type="button" aria-label="{label}" style="width: {size}px; height: {size}px; border: none; border-radius: {radius}px; background: {bg}; color: {color}; display: flex; align-items: center; justify-content: center; padding: 0; cursor: pointer; flex: none{extra}">{ic(n,16)}</button>'

def btn(label,icon=None,kind="ghost",h=36,grow=False,fs=13,extra=""):
    bg={"primary":ACC,"rec":REC,"ghost":SURF,"outline":"transparent","soft":"rgba(163,22,47,0.18)"}[kind]
    bd="1px solid rgba(255,255,255,0.14)" if kind=="outline" else ("1px solid rgba(240,166,177,0.25)" if kind=="soft" else "none")
    col= ROSE if kind=="soft" else TEXT
    g="flex: 1 1 0; min-width: 0;" if grow else "flex: none;"
    i=ic(icon,15) if icon else ""
    return f'<button type="button" style="{g} height: {h}px; border: {bd}; border-radius: {h//2 if h<=40 else 14}px; background: {bg}; color: {col}; display: flex; align-items: center; justify-content: center; gap: 8px; padding: 0 14px; font-size: {fs}px; font-weight: 600; letter-spacing: 0.2px; cursor: pointer; {FONT}; white-space: nowrap{extra}">{i}<span>{label}</span></button>'

def chip(label,icon=None,color=DIM,bg=SURF,fs=11):
    i=ic(icon,12) if icon else ""
    return f'<span style="display: inline-flex; align-items: center; gap: 4px; height: 20px; padding: 0 8px; border-radius: 10px; background: {bg}; color: {color}; font-size: {fs}px; font-weight: 500; white-space: nowrap">{i}{label}</span>'

def switch(on,label=None):
    tb=ACC if on else "rgba(255,255,255,0.14)"
    kx="18px" if on else "2px"
    return f'<span role="switch" aria-checked="{"true" if on else "false"}" aria-label="{label or ""}" style="position: relative; width: 38px; height: 22px; border-radius: 11px; background: {tb}; flex: none; display: block"><span style="position: absolute; top: 2px; left: {kx}; width: 18px; height: 18px; border-radius: 9px; background: #fff; box-shadow: 0 1px 3px rgba(0,0,0,0.4); display: block"></span></span>'

def label(t,fs=11):
    return f'<div style="font-size: {fs}px; font-weight: 600; letter-spacing: 0.9px; text-transform: uppercase; color: {FAINT}">{t}</div>'

def stepper(val,small=True):
    s=28
    return f'<div style="display: flex; align-items: center; gap: 2px; background: {SURF}; border-radius: 10px; padding: 2px; flex: none">{iconbtn("minus","Меньше",s,"transparent",DIM,8)}<div style="min-width: 64px; text-align: center; font-size: 13px; font-weight: 600; color: {TEXT}; font-variant-numeric: tabular-nums">{val}</div>{iconbtn("plus","Больше",s,"transparent",DIM,8)}</div>'

def slider(pct,valtxt=None,w="100%"):
    v=f'<div style="font-size: 12px; font-weight: 600; color: {TEXT}; min-width: 44px; text-align: right; font-variant-numeric: tabular-nums">{valtxt}</div>' if valtxt else ""
    return f'<div style="display: flex; align-items: center; gap: 10px; flex: 1 1 auto; min-width: 0"><div style="position: relative; flex: 1 1 auto; height: 6px; border-radius: 3px; background: rgba(255,255,255,0.1)"><div style="position: absolute; left: 0; top: 0; bottom: 0; width: {pct}%; border-radius: 3px; background: {ACC}"></div><div style="position: absolute; top: -5px; left: calc({pct}% - 8px); width: 16px; height: 16px; border-radius: 8px; background: #fff; box-shadow: 0 1px 4px rgba(0,0,0,0.5)"></div></div>{v}</div>'

def card(inner,title=None,pad=12,gap=8,extra=""):
    t=f'<div style="display: flex; align-items: center; justify-content: space-between">{label(title)}</div>' if title else ""
    return f'<section style="background: {SURF}; border: 1px solid {LINE}; border-radius: 14px; padding: {pad}px; display: flex; flex-direction: column; gap: {gap}px{extra}">{t}{inner}</section>'

def row(left,right,h=32,extra=""):
    return f'<div style="display: flex; align-items: center; justify-content: space-between; gap: 10px; min-height: {h}px{extra}">{left}{right}</div>'

def rtxt(t,fs=13,col=TEXT,w=500):
    return f'<span style="font-size: {fs}px; font-weight: {w}; color: {col}">{t}</span>'

def inp(ph,val="",icon=None,w=None):
    i=f'<span style="color: {FAINT}; display: flex">{ic(icon,15)}</span>' if icon else ""
    vv=f' value="{val}"' if val else ""
    return f'<label style="display: flex; align-items: center; gap: 8px; height: 38px; padding: 0 12px; border-radius: 11px; background: rgba(0,0,0,0.28); border: 1px solid {LINE}; flex: 1 1 auto; min-width: 0">{i}<input type="text" placeholder="{ph}"{vv} aria-label="{ph or val}" style="flex: 1 1 auto; min-width: 0; border: none; outline: none; background: transparent; color: {TEXT}; font-size: 13px; {FONT}"></label>'

def kbd(k):
    return f'<span style="display: inline-flex; align-items: center; justify-content: center; min-width: 24px; height: 20px; padding: 0 6px; border-radius: 6px; background: rgba(255,255,255,0.1); border: 1px solid rgba(255,255,255,0.12); border-bottom-width: 2px; font-size: 10px; font-weight: 700; color: {TEXT}">{k}</span>'

TABS=[("Main.dc.html","Дубли","layers"),("Camera.dc.html","Камера","video"),("Effects.dc.html","Эффекты","flame"),("Scene.dc.html","Сцена","clap"),("Weather.dc.html","Погода","cloud"),("Kits.dc.html","Наборы","bag"),("Team.dc.html","Команда","user"),("Options.dc.html","Опции","sliders")]

def panel(active,content,drill=False,gap=10):
    tabs=""
    for f,n,i in TABS:
        a = f==active
        st=f"background: {ACC}; color: #fff" if a else f"background: transparent; color: {DIM}"
        tabs+=f'<a href="{f}" style="flex: 1 1 0; min-width: 0; height: 34px; border-radius: 9px; {st}; display: flex; align-items: center; justify-content: center; font-size: 10px; font-weight: 600; text-decoration: none; white-space: nowrap">{n}</a>'
    hdr=f'''<header style="display: flex; align-items: center; gap: 12px; height: 44px">
<div style="width: 40px; height: 40px; border-radius: 12px; background: {ACC}; display: flex; align-items: center; justify-content: center; flex: none">{ic("video",20,"#fff")}</div>
<div style="display: flex; flex-direction: column; gap: 1px; flex: 1 1 auto; min-width: 0"><div style="font-size: 18px; font-weight: 700; color: {TEXT}; letter-spacing: 0.2px">VideoRecorder</div><div style="font-size: 12px; color: {DIM}">студия постановок · 3.1</div></div>
<div style="position: relative; display: flex">{iconbtn("undo","Отменить (3 шага)")}<span style="position: absolute; top: -4px; right: -4px; min-width: 16px; height: 16px; border-radius: 8px; background: {ACC}; color: #fff; font-size: 10px; font-weight: 700; display: flex; align-items: center; justify-content: center; padding: 0 4px">3</span></div>
{iconbtn("x","Закрыть панель")}
</header>'''
    scene=f'''<div style="display: flex; gap: 8px; align-items: center">
<button type="button" style="flex: 1 1 auto; min-width: 0; height: 40px; border: 1px solid {LINE}; border-radius: 12px; background: {SURF}; color: {TEXT}; display: flex; align-items: center; gap: 10px; padding: 0 12px; cursor: pointer; {FONT}">{ic("clap",16,ROSE)}<span style="font-size: 14px; font-weight: 600; flex: 1 1 auto; text-align: left; overflow: hidden; text-overflow: ellipsis; white-space: nowrap">Засада на снегу</span><span style="font-size: 12px; color: {DIM}">5 дублей · 00:24</span>{ic("down",15,DIM)}</button>
{iconbtn("plus","Новая сцена",40,SURF,TEXT,12)}{iconbtn("more","Действия со сценой",40,SURF,TEXT,12)}
</div>'''
    tabbar=f'<nav aria-label="Разделы" style="display: flex; gap: 4px; padding: 4px; border-radius: 12px; background: rgba(0,0,0,0.3)">{tabs}</nav>'
    return f'''<aside style="position: absolute; left: 20px; top: 20px; width: 440px; height: 680px; box-sizing: border-box; border-radius: 22px; {GLASS}; padding: 16px; display: flex; flex-direction: column; gap: 12px; {FONT}">
{hdr}{'' if drill else scene+tabbar}
<div style="flex: 1 1 auto; min-height: 0; overflow: hidden; display: flex; flex-direction: column; gap: {gap}px">{content}</div>
</aside>'''

def timeline(w_inner, playpct=26.25, compact=False):
    # 24s scene
    ticks=""
    for s in range(0,25,4):
        x=s/24*100
        ticks+=f'<span style="position: absolute; left: {x}%; transform: translateX(-50%); font-size: 10px; color: {FAINT}; font-variant-numeric: tabular-nums">0:{s:02d}</span>'
    lanes=""
    spans=[(0,12.3/24),(0,1.0),(2.5/24,(2.5+18.3)/24),(0,21.3/24),(0,24.3/24)]
    for k,(a,b) in enumerate(spans):
        op="0.35" if k==2 else "0.95"
        lanes+=f'<div style="position: absolute; left: {a*100}%; width: {(min(b,1)-a)*100}%; top: {8+k*6}px; height: 4px; border-radius: 2px; background: {TAKECOL[k]}; opacity: {op}"></div>'
    marks=""
    for t,n in [(4,"Взрыв у моста"),(11,"Хедшот")]:
        x=t/24*100
        marks+=f'<div style="position: absolute; left: {x}%; top: -2px; bottom: -2px; width: 2px; background: #f3e9e7; border-radius: 1px"></div>'
        if not compact:
            marks+=f'<div style="position: absolute; left: calc({x}% + 4px); top: -22px; display: flex; align-items: center; gap: 4px; height: 18px; padding: 0 7px; border-radius: 9px; background: rgba(243,233,231,0.14); color: #f3e9e7; font-size: 10px; font-weight: 600; white-space: nowrap">{ic("flag",10)}{n}</div>'
    fx=""
    for t in [4.2,8]:
        fx+=f'<div style="position: absolute; left: calc({t/24*100}% - 5px); bottom: 4px; width: 9px; height: 9px; transform: rotate(45deg); border-radius: 2px; background: {REC}"></div>'
    cams=""
    for t in [0,4,8]:
        cams+=f'<div style="position: absolute; left: calc({t/24*100}% - 4px); top: 0; width: 0; height: 0; border-left: 5px solid transparent; border-right: 5px solid transparent; border-top: 7px solid #ffffff"></div>'
    th=36 if compact else 44
    head=f'<div style="position: relative; height: 14px; margin-bottom: 6px">{ticks}</div>' if not compact else ""
    return f'''<div style="position: relative">{head}
<div style="position: relative; height: {th}px; border-radius: 11px; background: rgba(255,255,255,0.05); border: 1px solid {LINE}; overflow: visible">
<div style="position: absolute; left: 0; top: 0; bottom: 0; width: {playpct}%; border-radius: 11px 0 0 11px; background: rgba(163,22,47,0.22)"></div>
{lanes}{marks}{fx}{cams}
<div style="position: absolute; left: {playpct}%; top: -6px; bottom: -6px; width: 2px; margin-left: -1px; background: #fff; border-radius: 1px; box-shadow: 0 0 8px rgba(255,255,255,0.6)"></div>
<div style="position: absolute; left: calc({playpct}% - 6px); top: -10px; width: 12px; height: 12px; border-radius: 6px; background: #fff"></div>
</div></div>'''

def dock(playing=False, panel_open=True):
    x,w=(480,780) if panel_open else (300,680)
    pp=iconbtn("pause" if playing else "play","Пауза" if playing else "Пуск",44,ACC,"#fff",22)
    return f'''<section aria-label="Таймлайн и управление" style="position: absolute; left: {x}px; bottom: 20px; width: {w}px; box-sizing: border-box; border-radius: 22px; {GLASS}; padding: 14px 18px 14px; display: flex; flex-direction: column; gap: 12px; {FONT}">
<div style="padding-top: 18px">{timeline(w-36)}</div>
<div style="display: flex; align-items: center; gap: 8px">
{iconbtn("back","В начало",36,SURF,TEXT,18)}{pp}{iconbtn("stop","Стоп",36,SURF,TEXT,18)}
<div style="width: 1px; height: 24px; background: {LINE}; margin: 0 6px"></div>
<div style="display: flex; flex-direction: column; gap: 0"><span style="font-size: 18px; font-weight: 700; color: {TEXT}; font-variant-numeric: tabular-nums">00:06.3</span><span style="font-size: 11px; color: {DIM}; font-variant-numeric: tabular-nums">из 00:24.0 · {"идёт" if playing else "пауза"}</span></div>
<div style="flex: 1 1 auto"></div>
{btn("Слоу-мо 0.3×","clock","ghost",34,fs=12)}{btn("1×",None,"ghost",34,fs=12)}{btn("Камера","video","outline",34,fs=12)}
<button type="button" style="height: 40px; border: none; border-radius: 20px; background: {REC}; color: #fff; display: flex; align-items: center; gap: 8px; padding: 0 16px 0 12px; font-size: 13px; font-weight: 700; cursor: pointer; {FONT}; box-shadow: 0 6px 18px rgba(212,31,60,0.35)"><span style="width: 12px; height: 12px; border-radius: 6px; background: #fff; display: block"></span>Запись {kbd("F8")}</button>
</div></section>'''

def page(title, body, props_extra="", logic_extra="", rv_extra=""):
    props='{"accent":{"editor":"color","default":"#9e1530","options":["#9e1530","#7a1426","#b3263e","#5c0f1d"]}' + props_extra + ',"$preview":{"width":1280,"height":720}}'
    body=body.replace(ACC,"{{accent}}")
    return f'''<!doctype html>
<html lang="ru">
<head>
<meta charset="utf-8">
<title>{title}</title>
<script src="./support.js"></script>
</head>
<body>
<x-dc>
<helmet>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link href="https://fonts.googleapis.com/css2?family=Roboto+Condensed:wght@400;500;600;700&amp;display=swap" rel="stylesheet">
<style>
body{{margin:0}}
a{{color:#f0a6b1}}a:hover{{color:#ffc9d1}}
.vr-bg{{background-image:url({BG});background-size:cover;background-position:center}}
.vr-root input::placeholder{{color:#8c817f}}
</style>
</helmet>
<div class="vr-bg vr-root" style="width: 1280px; height: 720px; position: relative; overflow: hidden; {FONT}; color: {TEXT}">
{body}
</div>
</x-dc>
<script type="text/x-dc" data-dc-script data-props='{props}'>
class Component extends DCLogic {{
{logic_extra}
  renderVals() {{
    return {{ accent: this.props.accent ?? '#9e1530'{rv_extra} }};
  }}
}}
</script>
</body>
</html>
'''

def write(name, html):
    open(os.path.join(OUT,name),"w").write(html)

# ================= Main (Дубли)
takes=[("andrey","00:12.3",["рядом"],True,0,[]),("serega","00:15.3",[],True,1,[("loop","петля")]),
       ("Снайпер","00:18.3",[],False,2,[("clock","+2.5 с")]),("Водитель коптера","00:21.3",[],True,3,[("car","транспорт"),("mic","голос")]),
       ("Штурмовик","00:24.3",[],True,4,[("skull","смерть 9.5 с"),("shield","холостые")])]
def take_rows():
    out=""
    for i,(n,d,_,on,ci,tags) in enumerate(takes):
        cb=(f'<span role="checkbox" aria-checked="true" aria-label="В сцене" style="width: 22px; height: 22px; border-radius: 7px; background: {ACC}; display: flex; align-items: center; justify-content: center; flex: none; color: #fff">{ic("check",14,"#fff",2.6)}</span>' if on else
            f'<span role="checkbox" aria-checked="false" aria-label="В сцене" style="width: 22px; height: 22px; border-radius: 7px; border: 2px solid rgba(255,255,255,0.22); box-sizing: border-box; flex: none; display: block"></span>')
        ch="".join(chip(t,ic_) for ic_,t in tags)
        op="0.55" if not on else "1"
        hl=f"background: rgba(163,22,47,0.16); border: 1px solid rgba(240,166,177,0.22)" if i==0 else f"background: {SURF}; border: 1px solid {LINE}"
        out+=f'''<div style="display: flex; align-items: center; gap: 10px; height: 58px; padding: 0 8px 0 10px; border-radius: 14px; {hl}">
<span style="width: 4px; height: 30px; border-radius: 2px; background: {TAKECOL[ci]}; flex: none; display: block"></span>{cb}
<div style="flex: 1 1 auto; min-width: 0; display: flex; flex-direction: column; gap: 4px; opacity: {op}"><div style="font-size: 14px; font-weight: 600; color: {TEXT}; white-space: nowrap; overflow: hidden; text-overflow: ellipsis">{i+1}. {n}</div><div style="display: flex; gap: 4px; align-items: center">{chip(d,"clock")}{ch}</div></div>
{iconbtn("play","Пуск дубля",32,"transparent",TEXT,9)}<a href="TakeEditor.dc.html" aria-label="Изменить дубль" style="width: 32px; height: 32px; border-radius: 9px; display: flex; align-items: center; justify-content: center; color: {TEXT}; flex: none">{ic("edit",16)}</a>{iconbtn("rerec","Перезаписать",32,"transparent",TEXT,9)}{iconbtn("more","Ещё",32,"transparent",DIM,9)}
</div>'''
    return out
main_content=f'''<div style="display: flex; flex-direction: column; gap: 6px">{label("Имя следующего дубля")}<div style="display: flex; gap: 8px">{inp("например: Андрей, снайпер, водитель…","", "user")}{chip("авто: Дубль 6",None,DIM,SURF,11)}</div></div>
<div style="display: flex; align-items: center; justify-content: space-between; margin-top: 4px">{label("Дубли · 5")}<span style="font-size: 12px; color: {ROSE}; font-weight: 600">Включить все</span></div>
<div style="display: flex; flex-direction: column; gap: 6px">{take_rows()}</div>
<div style="flex: 1 1 auto"></div>
{btn("Показать траектории в мире","route","soft",38,False,13," ;width: 100%")}'''
write("Main.dc.html", page("Дубли — VideoRecorder", panel("Main.dc.html",main_content)+dock()))

# ================= TakeEditor
def seg(opts,active):
    o=""
    for x in opts:
        a=x==active
        st=f"background: {ACC}; color: #fff" if a else f"background: transparent; color: {DIM}"
        o+=f'<button type="button" style="height: 28px; min-width: 44px; border: none; border-radius: 8px; {st}; font-size: 12px; font-weight: 600; cursor: pointer; {FONT}">{x}</button>'
    return f'<div style="display: flex; gap: 2px; padding: 2px; border-radius: 10px; background: {SURF}">{o}</div>'
def sw_cell(icon,t,on):
    return f'<div style="display: flex; align-items: center; gap: 8px; height: 30px; padding: 0 2px; flex: 1 1 0; min-width: 0"><span style="color: {DIM}; display: flex">{ic(icon,14)}</span><span style="font-size: 13px; color: {TEXT}; flex: 1 1 auto; white-space: nowrap">{t}</span>{switch(on,t)}</div>'
trim=f'''<div style="position: relative; height: 34px; border-radius: 9px; background: rgba(0,0,0,0.3); overflow: hidden">
<div style="position: absolute; left: 6%; right: 9%; top: 0; bottom: 0; background: rgba(217,180,140,0.22); border-left: 3px solid {TAKECOL[3]}; border-right: 3px solid {TAKECOL[3]}"></div>
<div style="position: absolute; left: 0; width: 6%; top: 0; bottom: 0; background: repeating-linear-gradient(135deg, rgba(255,255,255,0.06) 0 4px, transparent 4px 8px)"></div>
<div style="position: absolute; right: 0; width: 9%; top: 0; bottom: 0; background: repeating-linear-gradient(135deg, rgba(255,255,255,0.06) 0 4px, transparent 4px 8px)"></div>
<div style="position: absolute; left: 10%; right: 12%; top: 15px; height: 4px; border-radius: 2px; background: rgba(217,180,140,0.6)"></div>
</div>'''
pos=""
for icn,t in [("pin","ТП к старту"),("flag","ТП к концу"),("move","Ко мне"),("rotate","90°"),("rotate","180°"),("mirror","Зеркало"),("copy","Копия")]:
    pos+=f'<button type="button" style="flex: 1 1 0; min-width: 0; height: 46px; border: none; border-radius: 10px; background: rgba(0,0,0,0.25); color: {TEXT}; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 3px; font-size: 10px; font-weight: 600; cursor: pointer; {FONT}">{ic(icn,15)}<span style="white-space: nowrap">{t}</span></button>'
te=f'''<div style="display: flex; align-items: center; gap: 8px; height: 24px"><a href="Main.dc.html" style="display: flex; align-items: center; gap: 4px; font-size: 12px; font-weight: 600; text-decoration: none">{ic("left",14)}Дубли</a><span style="color: {FAINT}; font-size: 12px">/</span><span style="font-size: 12px; color: {TEXT}; font-weight: 600">Дубль 4</span><span style="flex: 1 1 auto"></span>{chip("00:21.3","clock")}{chip("38 выстрелов","crosshair")}<a href="Appearance.dc.html" style="display: flex; align-items: center; gap: 4px; height: 22px; padding: 0 9px; border-radius: 11px; background: rgba(163,22,47,0.25); font-size: 11px; font-weight: 700; text-decoration: none">{ic("user",12)}Внешность</a></div>
<div style="display: flex; gap: 8px; align-items: center"><span style="width: 14px; height: 14px; border-radius: 7px; background: {TAKECOL[3]}; flex: none; display: block"></span>{inp("Имя дубля","Водитель коптера")}{iconbtn("play","Пуск дубля",38,ACC,"#fff",11)}</div>
{card(row(rtxt("Задержка старта"),stepper("+2.5 с"),30)+row(rtxt("Скорость"),seg(["0.5×","1×","1.5×","2×"],"1×"),30)+row(f'<div style="display: flex; align-items: center; gap: 8px">{switch(True,"Смерть по сценарию")}{rtxt("Смерть по сценарию")}</div>',stepper("09.5 с"),30),"Тайминг",10,6)}
{card(f'<div style="display: flex; gap: 14px">{sw_cell("check","В сцене",True)}{sw_cell("loop","Повтор",False)}</div><div style="display: flex; gap: 14px">{sw_cell("crosshair","Урон пуль",True)}{sw_cell("shield","Бессмертный",False)}</div><div style="display: flex; gap: 14px; align-items: center">{sw_cell("mic","Голос",True)}<div style="display: flex; align-items: center; gap: 8px; flex: 1 1 0; min-width: 0"><span style="color: {DIM}; display: flex">{ic("skull",14)}</span>{slider(100,"100 HP")}</div></div>',"Поведение",10,2)}
{card(trim+f'<div style="display: flex; justify-content: space-between; align-items: center"><div style="display: flex; align-items: center; gap: 6px">{ic("scissors",13,DIM)}{rtxt("Начало −1.3 с",12,DIM)}</div><div style="display: flex; align-items: center; gap: 6px">{rtxt("Конец −2.0 с",12,DIM)}{ic("scissors",13,DIM)}</div></div>',"Монтаж · тяни края",10,6)}
<div style="display: flex; gap: 6px">{pos}</div>
<div style="flex: 1 1 auto"></div>
<div style="display: flex; gap: 8px">{btn("Перезаписать","rerec","rec",40,True)}{btn("Продолжить запись","plus","outline",40,True)}</div>'''
write("TakeEditor.dc.html", page("Редактор дубля — VideoRecorder", panel("Main.dc.html",te,True,8)+dock()))

# ================= Camera
modes=[("route","Полёт по точкам",True),("user","Глазами бота",False),("nav","Из-за плеча",False),("orbit","Облёт",False),("tripod","Штатив",False)]
mg=""
for icn,t,a in modes:
    st=f"background: rgba(163,22,47,0.22); border: 1px solid rgba(240,166,177,0.45); color: #fff" if a else f"background: {SURF}; border: 1px solid {LINE}; color: {DIM}"
    mg+=f'<button type="button" style="flex: 1 1 0; min-width: 0; height: 64px; border-radius: 12px; {st}; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 6px; font-size: 11px; font-weight: 600; cursor: pointer; {FONT}; padding: 0 4px">{ic(icn,18)}<span style="text-align: center; line-height: 1.1">{t}</span></button>'
preview=f'''<div class="vr-bg" style="position: relative; width: 196px; height: 110px; border-radius: 12px; overflow: hidden; flex: none; background-position: 70% 40%; background-size: 260%; border: 1px solid rgba(255,255,255,0.14)">
<div style="position: absolute; inset: 0; background: linear-gradient(180deg, rgba(0,0,0,0) 60%, rgba(0,0,0,0.55))"></div>
<div style="position: absolute; left: 33.3%; top: 0; bottom: 0; width: 1px; background: rgba(255,255,255,0.35)"></div><div style="position: absolute; left: 66.6%; top: 0; bottom: 0; width: 1px; background: rgba(255,255,255,0.35)"></div>
<div style="position: absolute; top: 33.3%; left: 0; right: 0; height: 1px; background: rgba(255,255,255,0.35)"></div><div style="position: absolute; top: 66.6%; left: 0; right: 0; height: 1px; background: rgba(255,255,255,0.35)"></div>
<div style="position: absolute; left: 8px; top: 8px; display: flex; align-items: center; gap: 4px; height: 18px; padding: 0 7px; border-radius: 9px; background: rgba(0,0,0,0.55); font-size: 10px; font-weight: 700; color: #fff"><span style="width: 6px; height: 6px; border-radius: 3px; background: {REC}; display: block"></span>CAM 2 · 00:06.3</div>
<div style="position: absolute; right: 8px; bottom: 6px; font-size: 10px; color: rgba(255,255,255,0.8)">16:9 · сетка третей</div>
</div>'''
camright=f'''<div style="flex: 1 1 auto; min-width: 0; display: flex; flex-direction: column; gap: 8px; justify-content: center">
<div style="display: flex; align-items: center; justify-content: space-between">{rtxt("Сглаживание",12,DIM)}{rtxt("сплайн",12,TEXT,600)}</div>
<div style="display: flex; align-items: center; gap: 8px">{rtxt("Плавность",12,DIM)}{slider(70)}</div>
<div style="display: flex; align-items: center; justify-content: space-between">{rtxt("Свободный обзор мышью",12,DIM)}{switch(False,"Свободный обзор")}</div>
<div style="display: flex; align-items: center; justify-content: space-between">{rtxt("Показать путь в мире",12,DIM)}{switch(True,"Показать путь")}</div>
</div>'''
keys=""
for i,(t,sel) in enumerate([("00:00.0",False),("00:04.0",True),("00:08.0",False)]):
    hl=f"background: rgba(163,22,47,0.16); border: 1px solid rgba(240,166,177,0.22)" if sel else f"background: {SURF}; border: 1px solid {LINE}"
    keys+=f'''<div style="display: flex; align-items: center; gap: 8px; height: 40px; padding: 0 6px 0 10px; border-radius: 12px; {hl}">
<span style="width: 0; height: 0; border-left: 5px solid transparent; border-right: 5px solid transparent; border-top: 7px solid #fff; flex: none; display: block"></span>
<span style="font-size: 13px; font-weight: 700; color: {TEXT}; width: 52px">CAM {i+1}</span>
<span style="font-size: 13px; color: {TEXT}; font-variant-numeric: tabular-nums; width: 64px">{t}</span>
<div style="display: flex; gap: 2px">{btn("−1",None,"ghost",26,fs=11)}{btn("−0.1",None,"ghost",26,fs=11)}{btn("+0.1",None,"ghost",26,fs=11)}{btn("+1",None,"ghost",26,fs=11)}</div>
<span style="flex: 1 1 auto"></span>{iconbtn("pin","Телепорт к точке",28,"transparent",TEXT,8)}{iconbtn("x","Удалить точку",28,"transparent",DIM,8)}
</div>'''
cam=f'''<div style="display: flex; gap: 6px">{mg}</div>
<div style="display: flex; gap: 12px; align-items: stretch">{preview}{camright}</div>
<div style="display: flex; align-items: center; justify-content: space-between">{label("Точки пути · 3")}<span style="font-size: 12px; color: {ROSE}; font-weight: 600">Очистить</span></div>
<div style="display: flex; flex-direction: column; gap: 6px">{keys}</div>
{btn("Добавить точку отсюда · 00:06.3","plus","soft",38)}
<div style="flex: 1 1 auto"></div>
<div style="display: flex; gap: 8px; align-items: center">{btn("Смотреть через камеру","video","primary",42,True,14)}<div style="display: flex; align-items: center; gap: 4px; color: {DIM}; font-size: 11px">{kbd("F5")}<span>вкл/выкл</span></div></div>'''
write("Camera.dc.html", page("Кинокамера — VideoRecorder", panel("Camera.dc.html",cam)+dock()))

# ================= Effects
fxt=[("boom","Взрыв C4",True),("bolt","Ракета",False),("tank","Техника",False),("gas","Газ",False),("signal","Сигн. дым",False),("flame","Огонь",False),("smoke","Дымовая",False),("flare","Фальшфейер",False)]
fg=""
for icn,t,a in fxt:
    st=f"background: rgba(163,22,47,0.22); border: 1px solid rgba(240,166,177,0.45); color: #fff" if a else f"background: {SURF}; border: 1px solid {LINE}; color: {DIM}"
    fg+=f'<button type="button" style="height: 62px; border-radius: 12px; {st}; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 6px; font-size: 11px; font-weight: 600; cursor: pointer; {FONT}">{ic(icn,18)}<span>{t}</span></button>'
cues=""
for i,(icn,t,tm) in enumerate([("boom","Взрыв C4","00:04.2"),("flame","Огонь","00:08.0"),("smoke","Дымовая","00:12.5"),("bolt","Ракета","00:19.0")]):
    cues+=f'''<div style="display: flex; align-items: center; gap: 8px; height: 44px; padding: 0 6px 0 8px; border-radius: 12px; background: {SURF}; border: 1px solid {LINE}">
<span style="width: 30px; height: 30px; border-radius: 9px; background: rgba(212,31,60,0.18); color: {ROSE}; display: flex; align-items: center; justify-content: center; flex: none">{ic(icn,15)}</span>
<div style="display: flex; flex-direction: column; flex: 1 1 auto; min-width: 0"><span style="font-size: 13px; font-weight: 600; color: {TEXT}">{t}</span><span style="font-size: 11px; color: {DIM}">в 34 м от тебя</span></div>
<div style="display: flex; align-items: center; gap: 2px; background: rgba(0,0,0,0.25); border-radius: 9px; padding: 2px">{iconbtn("minus","Раньше",24,"transparent",DIM,7)}<span style="font-size: 12px; font-weight: 700; color: {TEXT}; font-variant-numeric: tabular-nums; width: 54px; text-align: center">{tm}</span>{iconbtn("plus","Позже",24,"transparent",DIM,7)}</div>
{iconbtn("play","Тест эффекта",30,"transparent",ROSE,8)}{iconbtn("x","Удалить",30,"transparent",DIM,8)}
</div>'''
fxc=f'''{label("Тип эффекта")}
<div style="display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 6px">{fg}</div>
{btn("Поставить туда, куда смотрю · 00:06.3","crosshair","primary",42,False,13)}
<div style="font-size: 12px; color: {DIM}; line-height: 1.4">Эффект встанет в точку прицела на текущее время сцены. Поставь сцену на паузу в нужный момент, и на таймлайне появится красный ромб.</div>
<div style="display: flex; align-items: center; justify-content: space-between">{label("На таймлайне · 4")}<span style="font-size: 12px; color: {ROSE}; font-weight: 600">Проиграть все</span></div>
<div style="display: flex; flex-direction: column; gap: 6px">{cues}</div>'''
write("Effects.dc.html", page("Эффекты — VideoRecorder", panel("Effects.dc.html",fxc)+dock()))

# ================= Scene
mk=""
for t,n in [("00:04.0","Взрыв у моста"),("00:11.0","Хедшот")]:
    mk+=f'''<div style="display: flex; align-items: center; gap: 8px; height: 32px; padding: 0 6px 0 10px; border-radius: 10px; background: rgba(0,0,0,0.22)">
<span style="color: #f3e9e7; display: flex">{ic("flag",13)}</span><span style="font-size: 12px; font-weight: 700; color: {TEXT}; font-variant-numeric: tabular-nums; width: 52px">{t}</span><span style="font-size: 13px; color: {TEXT}; flex: 1 1 auto">{n}</span>
{btn("Перейти",None,"ghost",26,fs=11)}{iconbtn("x","Удалить маркер",26,"transparent",DIM,7)}</div>'''
tr=""
for icn,t in [("move","Ко мне"),("rotate","Повернуть 90°"),("rotate","Повернуть 180°"),("mirror","Зеркало")]:
    tr+=f'<button type="button" style="flex: 1 1 0; min-width: 0; height: 40px; border: none; border-radius: 10px; background: rgba(0,0,0,0.25); color: {TEXT}; display: flex; align-items: center; justify-content: center; gap: 6px; font-size: 11px; font-weight: 600; cursor: pointer; {FONT}">{ic(icn,14)}<span style="white-space: nowrap">{t}</span></button>'
wx=""
for icn,t,a in [("sun","Ясно",True),("cloud","Облачно",False),("fog","Туман",False),("rain","Дождь",False),("bolt","Гроза",False)]:
    st=f"background: {ACC}; color: #fff" if a else f"background: rgba(0,0,0,0.25); color: {DIM}"
    wx+=f'<button type="button" style="flex: 1 1 0; min-width: 0; height: 32px; border: none; border-radius: 9px; {st}; display: flex; align-items: center; justify-content: center; gap: 5px; font-size: 11px; font-weight: 600; cursor: pointer; {FONT}">{ic(icn,13)}{t}</button>'
code=f'<div style="display: flex; align-items: center; gap: 8px; height: 38px; padding: 0 6px 0 12px; border-radius: 11px; background: rgba(0,0,0,0.28); border: 1px dashed rgba(240,166,177,0.4); flex: 1 1 0; min-width: 0"><span style="font-size: 16px; font-weight: 700; letter-spacing: 3px; color: {ROSE}; flex: 1 1 auto">K7QX2M</span>{iconbtn("copy","Копировать код",28,"transparent",TEXT,8)}</div>'
sc=f'''<div style="display: flex; gap: 8px">{inp("Название сцены","Засада на снегу","clap")}{iconbtn("copy","Копия сцены",38,SURF,TEXT,11)}</div>
{card(f'<div style="display: flex; gap: 6px">{inp("Название маркера…","","tag")}{btn("+ в 00:06.3",None,"primary",38,fs=12)}</div>'+mk,"Маркеры",8,6)}
{card(f'<div style="display: flex; gap: 6px">{tr}</div>',"Вся сцена · вокруг тебя",8,8)}
{card(f'<div style="display: flex; align-items: center; gap: 10px"><span style="color: {ROSE}; display: flex">{ic("sun",15)}</span>{slider(73,"17:30")}{btn("Сброс",None,"ghost",28,fs=11)}</div><div style="display: flex; gap: 4px">{wx}</div>',"Атмосфера · время видишь только ты",8,8)}
{card(f'<div style="display: flex; gap: 6px; align-items: center">{code}{inp("Ввести код","", "import")}</div>',"Поделиться сценой",8,8)}'''
write("Scene.dc.html", page("Сцена — VideoRecorder", panel("Scene.dc.html",sc,False,8)+dock()))

# ================= Options (interactive switches)
sw_tpl = ('<sc-for list="{{opts}}" as="item" hint-placeholder-count="5">'
  '<div style="display: flex; align-items: center; gap: 10px; height: 32px">'
  '<div style="display: flex; flex-direction: column; flex: 1 1 auto; min-width: 0"><span style="font-size: 13px; font-weight: 600; color: '+TEXT+'">{{item.title}}</span><span style="font-size: 11px; color: '+DIM+'">{{item.sub}}</span></div>'
  '<button type="button" role="switch" aria-checked="{{item.aria}}" aria-label="{{item.title}}" onClick="{{item.toggle}}" style="position: relative; width: 40px; height: 24px; border: none; border-radius: 12px; background: {{item.track}}; padding: 0; cursor: pointer; flex: none">'
  '<span style="position: absolute; top: 3px; left: {{item.left}}; width: 18px; height: 18px; border-radius: 9px; background: #fff; box-shadow: 0 1px 3px rgba(0,0,0,0.4); display: block"></span></button>'
  '</div></sc-for>')
keys_=""
for k,t in [("F5","Кинокамера"),("F6","Панель"),("F7","Сцена: пуск/стоп"),("F8","Запись"),("F9","Пауза"),("F10","Слоу-мо")]:
    keys_+=f'<div style="display: flex; align-items: center; gap: 8px; height: 30px; padding: 0 8px; border-radius: 9px; background: rgba(0,0,0,0.22)">{kbd(k)}<span style="font-size: 12px; color: {TEXT}">{t}</span></div>'
opt=f'''{card(sw_tpl,"Запись",10,0)}
{card(row(rtxt("Старт сцены с"),stepper("00:00"),28)+row(rtxt("Скорость сцены"),stepper("1.00×"),28)+row(rtxt("Скорость слоу-мо"),stepper("0.30×"),28),"Воспроизведение",10,6)}
{card(f'<div style="display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 6px">{keys_}</div><div style="display: flex; align-items: center; gap: 8px"><span style="font-size: 11px; color: {DIM}; flex: 1 1 auto">Вставь в консоль F1 одной строкой</span>{btn("Скопировать бинды","copy","ghost",28,fs=11)}</div>',"Горячие клавиши",10,8)}'''
logic = '''  constructor(props) {
    super(props);
    this.state = { clean: false, god: true, ammo: true, voice: true, tags: false };
  }'''
rv = """, opts: [
      ['clean', 'Чистый кадр', 'прячет весь HUD плагина'],
      ['god', 'Бессмертие при записи', 'боты не убьют тебя на дубле'],
      ['ammo', 'Бесконечные патроны', 'магазин не кончается'],
      ['voice', 'Записывать голос', 'боты повторят твои реплики'],
      ['tags', 'Подписи над ботами', 'видишь только ты']
    ].map(([k, title, sub]) => {
      const on = !!this.state[k];
      return { title, sub, aria: on ? 'true' : 'false', track: on ? (this.props.accent ?? '#9e1530') : 'rgba(255,255,255,0.16)', left: on ? '19px' : '3px', toggle: () => this.setState({ [k]: !on }) };
    })"""
write("Options.dc.html", page("Опции — VideoRecorder", panel("Options.dc.html",opt)+dock(), "", logic, rv))

# ================= Recording
ring_pct=0.66
rec=f'''<div style="position: absolute; inset: 0; background: radial-gradient(ellipse at center, rgba(0,0,0,0) 45%, rgba(0,0,0,0.45) 100%)"></div>
<div style="position: absolute; left: 0; right: 0; top: 0; height: 4px; background: {REC}"></div>
<div style="position: absolute; right: 20px; top: 20px; display: flex; align-items: center; gap: 10px; height: 40px; padding: 0 16px; border-radius: 20px; {GLASS}; {FONT}"><span style="width: 10px; height: 10px; border-radius: 5px; background: {REC}; display: block; box-shadow: 0 0 10px {REC}"></span><span style="font-size: 15px; font-weight: 700; letter-spacing: 1px">REC</span><span style="font-size: 15px; font-weight: 600; color: {DIM}; font-variant-numeric: tabular-nums">00:00</span></div>
<div style="position: absolute; left: 50%; top: 50%; transform: translate(-50%, -58%); display: flex; flex-direction: column; align-items: center; gap: 18px; {FONT}">
<div style="position: relative; width: 168px; height: 168px; border-radius: 84px; {GLASS}; display: flex; align-items: center; justify-content: center">
<svg width="168" height="168" viewBox="0 0 168 168" aria-hidden="true" style="position: absolute; inset: 0"><circle cx="84" cy="84" r="76" fill="none" stroke="rgba(255,255,255,0.1)" stroke-width="6"></circle><circle cx="84" cy="84" r="76" fill="none" stroke="{REC}" stroke-width="6" stroke-linecap="round" stroke-dasharray="{477.5*ring_pct:.1f} 477.5" transform="rotate(-90 84 84)"></circle></svg>
<div style="display: flex; flex-direction: column; align-items: center; gap: 0"><span style="font-size: 76px; font-weight: 700; line-height: 1; color: #fff">3</span><span style="font-size: 11px; font-weight: 700; letter-spacing: 2px; color: {ROSE}">ЗАПИСЬ</span></div>
</div>
<div style="display: flex; align-items: center; gap: 10px; height: 40px; padding: 0 16px; border-radius: 20px; {GLASS}"><span style="width: 10px; height: 10px; border-radius: 5px; background: {TAKECOL[0]}; display: block"></span><span style="font-size: 14px; font-weight: 700">Дубль 6 · andrey</span><span style="font-size: 13px; color: {DIM}">одновременно играют 4 актёра</span></div>
</div>
<div style="position: absolute; left: 50%; bottom: 24px; transform: translateX(-50%); display: flex; align-items: center; gap: 14px; height: 40px; padding: 0 16px; border-radius: 20px; {GLASS}; {FONT}; font-size: 12px; color: {DIM}">
<span style="display: flex; align-items: center; gap: 6px">{kbd("F8")}стоп и сохранить</span><span style="display: flex; align-items: center; gap: 6px">{kbd("F9")}пауза сцены</span><span style="display: flex; align-items: center; gap: 6px">{kbd("F6")}панель</span>
<a href="Main.dc.html" style="display: flex; align-items: center; gap: 6px; height: 28px; padding: 0 12px; border-radius: 14px; background: {REC}; color: #fff; font-weight: 700; text-decoration: none">{ic("stop",12,"#fff")}Стоп</a></div>'''
write("Recording.dc.html", page("Запись — VideoRecorder", rec))

# ================= Playback (panel hidden)
pb=f'''<div style="position: absolute; right: 20px; top: 20px; width: 240px; box-sizing: border-box; padding: 12px 14px; border-radius: 18px; {GLASS}; {FONT}; display: flex; flex-direction: column; gap: 8px">
<div style="display: flex; align-items: center; gap: 8px"><span style="width: 8px; height: 8px; border-radius: 4px; background: {ROSE}; display: block"></span><span style="font-size: 12px; font-weight: 700; letter-spacing: 1px; color: {ROSE}">СЦЕНА ИДЁТ</span><span style="flex: 1 1 auto"></span><span style="font-size: 13px; font-weight: 700; font-variant-numeric: tabular-nums">00:06 / 00:24</span></div>
<div style="height: 4px; border-radius: 2px; background: rgba(255,255,255,0.1)"><div style="width: 26%; height: 4px; border-radius: 2px; background: {ACC}"></div></div>
<div style="font-size: 11px; color: {DIM}">Засада на снегу · 4 актёра · камера: полёт</div>
</div>
<div style="position: absolute; left: 742px; top: 360px; display: flex; flex-direction: column; align-items: center; gap: 4px; {FONT}"><span style="display: flex; align-items: center; gap: 5px; height: 22px; padding: 0 9px; border-radius: 11px; background: rgba(18,14,14,0.75); font-size: 11px; font-weight: 700; color: #fff"><span style="width: 7px; height: 7px; border-radius: 4px; background: {TAKECOL[1]}; display: block"></span>2 · serega</span><span style="width: 1px; height: 18px; background: rgba(255,255,255,0.5); display: block"></span></div>
<div style="position: absolute; left: 980px; top: 300px; display: flex; flex-direction: column; align-items: center; gap: 4px; {FONT}"><span style="display: flex; align-items: center; gap: 5px; height: 22px; padding: 0 9px; border-radius: 11px; background: rgba(212,31,60,0.85); font-size: 11px; font-weight: 700; color: #fff">{ic("boom",11,"#fff")}Взрыв C4 · через 0.4 с</span></div>
{dock(True, False)}
<a href="Main.dc.html" style="position: absolute; left: 20px; top: 20px; display: flex; align-items: center; gap: 8px; height: 40px; padding: 0 14px 0 6px; border-radius: 20px; {GLASS}; {FONT}; color: {TEXT}; font-size: 13px; font-weight: 600; text-decoration: none"><span style="width: 30px; height: 30px; border-radius: 15px; background: {ACC}; display: flex; align-items: center; justify-content: center">{ic("video",15,"#fff")}</span>VideoRecorder {kbd("F6")}</a>'''
write("Playback.dc.html", page("Просмотр сцены — VideoRecorder", pb))


# ================= Kits
def slot(icn,badge=None,skin=False):
    b=f'<span style="position: absolute; right: 3px; bottom: 1px; font-size: 9px; font-weight: 700; color: {TEXT}">{badge}</span>' if badge else ""
    sk=f'<span style="position: absolute; left: 3px; top: 3px; width: 6px; height: 6px; border-radius: 3px; background: {ROSE}; display: block"></span>' if skin else ""
    return f'<div style="position: relative; width: 38px; height: 38px; border-radius: 9px; background: rgba(0,0,0,0.3); border: 1px solid {LINE}; display: flex; align-items: center; justify-content: center; color: #e7dcd9; flex: none">{ic(icn,20)}{sk}{b}</div>'
kitsd=[("Снайпер в снегу","14 предметов · 6 одежды · 5 скинов",[("rifle","",True),("ammo","120",False),("helmet","",True),("shirt","",True),("med","3",False)],True),
       ("Штурм МВК","19 предметов · 7 одежды · 2 скина",[("rifle","",True),("rifle","",False),("ammo","256",False),("helmet","",False),("med","5",False)],False),
       ("Водитель коптера","8 предметов · 4 одежды",[("shirt","",False),("helmet","",False),("pick","",False),("med","1",False)],False)]
cards=""
for i,(n,meta,items,sel) in enumerate(kitsd):
    hl=f"background: rgba(163,22,47,0.16); border: 1px solid rgba(240,166,177,0.25)" if sel else f"background: {SURF}; border: 1px solid {LINE}"
    sl="".join(slot(a,b,c) for a,b,c in items)
    more=f'<div style="width: 38px; height: 38px; border-radius: 9px; border: 1px dashed {LINE}; display: flex; align-items: center; justify-content: center; font-size: 11px; color: {DIM}; flex: none">+{max(0,int(meta.split()[0])-len(items))}</div>' if int(meta.split()[0])>len(items) else ""
    cards+=f"""<div style="border-radius: 14px; {hl}; padding: 10px; display: flex; flex-direction: column; gap: 8px">
<div style="display: flex; align-items: center; gap: 8px"><span style="color: {ROSE}; display: flex">{ic("bag",15)}</span><div style="display: flex; flex-direction: column; flex: 1 1 auto; min-width: 0"><span style="font-size: 14px; font-weight: 700; color: {TEXT}">{n}</span><span style="font-size: 11px; color: {DIM}">{meta}</span></div>{btn("Надеть","shirt","primary",30,fs=12)}{iconbtn("more","Ещё: добавить, обновить, на дубль, удалить",30,"transparent",DIM,9)}</div>
<div style="display: flex; gap: 6px; align-items: center">{sl}{more}<span style="flex: 1 1 auto"></span>{btn("На дубль","user","ghost",28,fs=11)}</div>
</div>"""
kc=f"""<div style="display: flex; flex-direction: column; gap: 6px">{label("Сохранить текущий инвентарь")}<div style="display: flex; gap: 8px">{inp("Название набора: снайпер, МВК…","","tag")}{btn("Сохранить","plus","primary",38,fs=13)}</div>
<div style="font-size: 11px; color: {DIM}">Запоминаются одежда, пояс и рюкзак со скинами, модулями, патронами и прочностью.</div></div>
<div style="display: flex; align-items: center; justify-content: space-between">{label("Мои наборы · 4 из 30")}<span style="font-size: 12px; color: {ROSE}; font-weight: 600">Поделиться кодом</span></div>
<div style="display: flex; flex-direction: column; gap: 8px">{cards}</div>"""
write("Kits.dc.html", page("Наборы инвентаря — VideoRecorder", panel("Kits.dc.html",kc)+dock()))

# ================= Appearance (drill-in from take editor)
faces=""
for i,(sel) in enumerate([False,True,False,False,False]):
    st=f"border: 2px solid {ROSE}; background: rgba(163,22,47,0.22)" if sel else f"border: 1px solid {LINE}; background: {SURF}"
    faces+=f'<button type="button" aria-label="Лицо {41+i}" style="flex: 1 1 0; min-width: 0; height: 74px; border-radius: 12px; {st}; color: {TEXT if sel else DIM}; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 4px; cursor: pointer; {FONT}">{ic("user",26)}<span style="font-size: 11px; font-weight: 700">#{41+i:05d}</span></button>'
players=""
for n,sel in [("kio",False),("Andrey_RU",True),("Serega",False),("sniper228",False),("lera",False),("TTV_mops",False)]:
    st=f"background: {ACC}; color: #fff" if sel else f"background: {SURF}; color: {TEXT}"
    players+=f'<button type="button" style="height: 32px; border: none; border-radius: 16px; {st}; display: flex; align-items: center; gap: 6px; padding: 0 12px 0 4px; font-size: 12px; font-weight: 600; cursor: pointer; {FONT}"><span style="width: 24px; height: 24px; border-radius: 12px; background: rgba(0,0,0,0.3); display: flex; align-items: center; justify-content: center">{ic("user",13)}</span>{n}</button>'
ap=f"""<div style="display: flex; align-items: center; gap: 8px; height: 24px"><a href="TakeEditor.dc.html" style="display: flex; align-items: center; gap: 4px; font-size: 12px; font-weight: 600; text-decoration: none">{ic("left",14)}Дубль 4</a><span style="color: {FAINT}; font-size: 12px">/</span><span style="font-size: 12px; color: {TEXT}; font-weight: 600">Внешность</span></div>
<div style="display: flex; gap: 8px; align-items: center"><span style="width: 14px; height: 14px; border-radius: 7px; background: {TAKECOL[3]}; flex: none; display: block"></span>{inp("Ник бота","Andrey_RU","tag")}{btn("Ник игрока","copy","ghost",38,fs=12)}</div>
{card(seg(["Своё лицо","Как у автора","Как у игрока"],"Как у игрока").replace("min-width: 44px","flex: 1 1 0")+f'<div style="display: flex; flex-wrap: wrap; gap: 6px">{players}</div><div style="display: flex; gap: 6px">{inp("или SteamID64: 7656119…","","import")}{btn("Применить",None,"ghost",38,fs=12)}</div>',"Чья внешность",10,8)}
{card(f'<div style="display: flex; gap: 6px">{faces}</div><div style="display: flex; gap: 6px">{btn("Случайное лицо","rerec","ghost",32,True,12)}{btn("Только мужские",None,"ghost",32,True,12)}</div>',"Своё лицо · листай, манекен покажет",10,8,"; opacity: 0.55")}
<div style="display: flex; align-items: center; gap: 10px; padding: 8px 10px; border-radius: 12px; background: {SURF}; border: 1px solid {LINE}">{switch(False,"Точная копия")}<div style="display: flex; flex-direction: column; flex: 1 1 auto"><span style="font-size: 13px; font-weight: 600">Точная копия по SteamID</span><span style="font-size: 11px; color: {DIM}">только если лицо не совпало, путает другие плагины</span></div></div>
<div style="flex: 1 1 auto"></div>
<div style="display: flex; gap: 8px">{btn("Примерить перед собой","user","primary",40,True)}{btn("Ко всем дублям","layers","outline",40,True)}</div>"""
mannequin=f'''<div style="position: absolute; left: 640px; top: 260px; display: flex; flex-direction: column; align-items: center; gap: 6px; {FONT}">
<span style="display: flex; align-items: center; gap: 6px; height: 26px; padding: 0 12px; border-radius: 13px; {GLASS}; font-size: 12px; font-weight: 700; color: #fff">{ic("user",13)}Примерка · Andrey_RU · 22 с</span>
<span style="width: 1px; height: 40px; background: rgba(255,255,255,0.6); display: block"></span>
<span style="width: 70px; height: 12px; border-radius: 35px; background: rgba(240,166,177,0.35); display: block"></span></div>'''
write("Appearance.dc.html", page("Внешность бота — VideoRecorder", panel("Main.dc.html",ap,True,8)+mannequin+dock()))

# ================= Team («Мотор для всех»)
def member(name,online,state,state_col,role_actor,ready=None,kick=False,me=False):
    dot="#7fd18b" if online else "#6b6260"
    role_bg=ACC if role_actor else SURF
    r=f'<button type="button" style="height: 28px; min-width: 82px; border: none; border-radius: 14px; background: {role_bg}; color: #fff; font-size: 11px; font-weight: 700; cursor: pointer; {FONT}">{"Актёр" if role_actor else "Оператор"}</button>'
    rd=""
    if ready is not None:
        rd=f'<button type="button" style="height: 28px; min-width: 82px; border: none; border-radius: 14px; background: {"rgba(127,209,139,0.2)" if ready else SURF}; color: {"#a6e3b0" if ready else DIM}; font-size: 11px; font-weight: 700; cursor: pointer; {FONT}; display: flex; align-items: center; justify-content: center; gap: 4px">{ic("check",12) if ready else ""}{"Готов" if ready else "Не готов"}</button>'
    k=iconbtn("x","Убрать из сцены",28,"transparent",DIM,8) if kick else '<span style="width: 28px; flex: none; display: block"></span>'
    hl="background: rgba(163,22,47,0.14); border: 1px solid rgba(240,166,177,0.2)" if me else f"background: {SURF}; border: 1px solid {LINE}"
    return (f'<div style="display: flex; align-items: center; gap: 10px; height: 48px; padding: 0 8px 0 10px; border-radius: 14px; {hl}">'
        f'<span style="position: relative; width: 32px; height: 32px; border-radius: 16px; background: rgba(0,0,0,0.3); display: flex; align-items: center; justify-content: center; color: {TEXT}; flex: none">{ic("user",16)}<span style="position: absolute; right: -1px; bottom: -1px; width: 10px; height: 10px; border-radius: 5px; background: {dot}; border: 2px solid #1a1616; display: block"></span></span>'
        f'<div style="display: flex; flex-direction: column; flex: 1 1 auto; min-width: 0"><span style="font-size: 14px; font-weight: 600; color: {TEXT}">{name}</span><span style="font-size: 11px; color: {state_col}; font-weight: 600">{state}</span></div>'
        f'{r}{rd}{k}</div>')
invites=""
for n,pending in [("sniper228",True),("lera",False),("TTV_mops",False)]:
    if pending:
        invites+=f'<span style="height: 32px; display: flex; align-items: center; gap: 6px; padding: 0 12px; border-radius: 16px; background: rgba(0,0,0,0.25); color: {DIM}; font-size: 12px; font-weight: 600">{ic("clock",13)}ждём: {n}</span>'
    else:
        invites+=f'<button type="button" style="height: 32px; border: 1px dashed rgba(255,255,255,0.2); border-radius: 16px; background: transparent; color: {TEXT}; display: flex; align-items: center; gap: 6px; padding: 0 12px; font-size: 12px; font-weight: 600; cursor: pointer; {FONT}">{ic("plus",13)}{n}</button>'
motor=(f'<div style="border-radius: 16px; background: rgba(212,31,60,0.14); border: 1px solid rgba(240,166,177,0.25); padding: 14px; display: flex; align-items: center; gap: 12px">'
 f'<div style="display: flex; flex-direction: column; gap: 4px; flex: 1 1 auto; min-width: 0"><span style="font-size: 16px; font-weight: 700; letter-spacing: 0.5px">Мотор для всех</span><span style="font-size: 11px; color: {DIM}; line-height: 1.35">Один отсчёт 5 с на всю команду. Каждый актёр пишет свой дубль синхронно с остальными.</span></div>'
 f'<a href="Motor.dc.html" style="height: 48px; border-radius: 24px; background: {REC}; color: #fff; display: flex; align-items: center; gap: 10px; padding: 0 18px; font-size: 15px; font-weight: 700; text-decoration: none; box-shadow: 0 8px 22px rgba(212,31,60,0.35); white-space: nowrap"><span style="width: 12px; height: 12px; border-radius: 6px; background: #fff; display: block"></span>Мотор! · 2 акт.</a></div>')
tm=(motor
 + f'<div style="display: flex; align-items: center; justify-content: space-between">{label("В сцене · 3")}{kbd("F11")}</div>'
 + '<div style="display: flex; flex-direction: column; gap: 6px">'
 + member("kio · владелец (ты)",True,"готов","#7fd18b",True,True,False,True)
 + member("Andrey_RU",True,"готов","#7fd18b",True,None,True)
 + member("Serega",True,"оператор · снимает камерой",DIM,False,None,True)
 + '</div>'
 + f'<div style="display: flex; align-items: center; justify-content: space-between; margin-top: 4px">{label("Пригласить в сцену")}<span style="font-size: 11px; color: {DIM}">игроки онлайн с доступом</span></div>'
 + f'<div style="display: flex; flex-wrap: wrap; gap: 6px">{invites}</div>'
 + '<div style="flex: 1 1 auto"></div>'
 + f'<div style="font-size: 11px; color: {DIM}; line-height: 1.4">Друг увидит окно приглашения: <a href="Invite.dc.html">как оно выглядит</a>. Дубли с «Мотора» сразу похожи на своего автора и подписаны его ником.</div>')
write("Team.dc.html", page("Команда — VideoRecorder", panel("Team.dc.html",tm)+dock()))

# ================= Invite
inv=(f'<div style="position: absolute; inset: 0; background: rgba(8,6,6,0.45)"></div>'
 f'<div role="dialog" aria-label="Приглашение на съёмку" style="position: absolute; left: 50%; top: 50%; transform: translate(-50%, -50%); width: 480px; box-sizing: border-box; border-radius: 24px; {GLASS}; padding: 20px; display: flex; flex-direction: column; gap: 14px; {FONT}">'
 f'<div style="display: flex; align-items: center; gap: 12px"><div style="width: 44px; height: 44px; border-radius: 22px; background: {REC}; display: flex; align-items: center; justify-content: center; flex: none">{ic("video",20,"#fff")}</div><div style="display: flex; flex-direction: column; gap: 2px; flex: 1 1 auto"><span style="font-size: 18px; font-weight: 700">Приглашение на съёмку</span><span style="font-size: 12px; color: {DIM}"><b style="color: {TEXT}">kio</b> зовёт тебя сниматься в своей сцене</span></div><span style="font-size: 11px; color: {DIM}; font-variant-numeric: tabular-nums; display: flex; align-items: center; gap: 4px">{ic("clock",12)}0:48</span></div>'
 f'<div class="vr-bg" style="position: relative; height: 120px; border-radius: 16px; overflow: hidden; background-position: 60% 45%; background-size: 220%; border: 1px solid rgba(255,255,255,0.1)">'
 '<div style="position: absolute; inset: 0; background: linear-gradient(180deg, rgba(0,0,0,0) 30%, rgba(0,0,0,0.75))"></div>'
 '<div style="position: absolute; left: 14px; bottom: 12px; right: 14px; display: flex; flex-direction: column; gap: 4px"><span style="font-size: 17px; font-weight: 700; color: #fff">«Засада на снегу»</span><span style="font-size: 11px; color: rgba(255,255,255,0.85)">5 дублей · длина 00:24 · в команде: kio, Andrey_RU, Serega</span></div></div>'
 f'<div style="display: flex; flex-direction: column; gap: 8px">{label("Твоя роль")}<div style="display: flex; gap: 8px">'
 f'<button type="button" style="flex: 1 1 0; min-width: 0; border-radius: 14px; background: rgba(163,22,47,0.24); border: 1px solid rgba(240,166,177,0.5); color: #fff; display: flex; align-items: center; gap: 10px; padding: 10px 12px; text-align: left; cursor: pointer; {FONT}">{ic("user",18)}<span style="display: flex; flex-direction: column; gap: 2px"><span style="font-size: 14px; font-weight: 700">Актёр</span><span style="font-size: 11px; color: {ROSE}">играешь в кадре, бот повторит тебя</span></span></button>'
 f'<button type="button" style="flex: 1 1 0; min-width: 0; border-radius: 14px; background: {SURF}; border: 1px solid {LINE}; color: {TEXT}; display: flex; align-items: center; gap: 10px; padding: 10px 12px; text-align: left; cursor: pointer; {FONT}">{ic("video",18)}<span style="display: flex; flex-direction: column; gap: 2px"><span style="font-size: 14px; font-weight: 700">Оператор</span><span style="font-size: 11px; color: {DIM}">не пишешься, снимаешь камерой</span></span></button>'
 '</div></div>'
 f'<div style="display: flex; gap: 8px"><a href="Team.dc.html" style="flex: 1 1 0; height: 46px; border-radius: 23px; background: {REC}; color: #fff; display: flex; align-items: center; justify-content: center; gap: 8px; font-size: 15px; font-weight: 700; text-decoration: none">{ic("check",16,"#fff")}Присоединиться</a>{btn("Отклонить",None,"ghost",46,True,15)}</div>'
 f'<div style="font-size: 11px; color: {DIM}; text-align: center">Можно принять и командой /vr join</div></div>')
write("Invite.dc.html", page("Приглашение — VideoRecorder", inv))

# ================= Motor (групповой отсчёт)
actors=""
for n,c,me in [("kio","#ff5a6e",False),("Andrey_RU","#f2a0ad",True)]:
    actors+=f'<span style="display: flex; align-items: center; gap: 6px; height: 28px; padding: 0 12px; border-radius: 14px; background: {"rgba(212,31,60,0.3)" if me else "rgba(255,255,255,0.08)"}; font-size: 12px; font-weight: 700"><span style="width: 8px; height: 8px; border-radius: 4px; background: {c}; display: block"></span>{n}{" · ты" if me else ""}</span>'
mo=('<div style="position: absolute; inset: 0; background: radial-gradient(ellipse at center, rgba(0,0,0,0) 40%, rgba(0,0,0,0.5) 100%)"></div>'
 f'<div style="position: absolute; left: 0; right: 0; top: 0; height: 4px; background: {REC}"></div>'
 f'<div style="position: absolute; left: 50%; top: 50%; transform: translate(-50%, -56%); display: flex; flex-direction: column; align-items: center; gap: 16px; {FONT}">'
 f'<span style="font-size: 13px; font-weight: 700; letter-spacing: 3px; color: {ROSE}">МОТОР ДЛЯ ВСЕХ</span>'
 f'<div style="position: relative; width: 176px; height: 176px; border-radius: 88px; {GLASS}; display: flex; align-items: center; justify-content: center">'
 f'<svg width="176" height="176" viewBox="0 0 176 176" aria-hidden="true" style="position: absolute; inset: 0"><circle cx="88" cy="88" r="80" fill="none" stroke="rgba(255,255,255,0.1)" stroke-width="6"></circle><circle cx="88" cy="88" r="80" fill="none" stroke="{REC}" stroke-width="6" stroke-linecap="round" stroke-dasharray="301.6 502.7" transform="rotate(-90 88 88)"></circle></svg>'
 '<span style="font-size: 84px; font-weight: 700; line-height: 1; color: #fff">3</span></div>'
 '<span style="font-size: 18px; font-weight: 700">Ты в кадре — приготовься</span>'
 f'<div style="display: flex; gap: 6px; align-items: center"><span style="font-size: 12px; color: {DIM}">пишут:</span>{actors}<span style="font-size: 12px; color: {DIM}">· снимает: Serega</span></div></div>'
 f'<div style="position: absolute; left: 50%; bottom: 24px; transform: translateX(-50%); display: flex; align-items: center; gap: 14px; height: 40px; padding: 0 16px; border-radius: 20px; {GLASS}; {FONT}; font-size: 12px; color: {DIM}">'
 f'<span style="display: flex; align-items: center; gap: 6px">{kbd("F11")}отменить отсчёт / стоп для всех</span><span style="display: flex; align-items: center; gap: 6px">{kbd("F8")}то же самое</span>'
 f'<a href="Team.dc.html" style="display: flex; align-items: center; gap: 6px; height: 28px; padding: 0 12px; border-radius: 14px; background: {REC}; color: #fff; font-weight: 700; text-decoration: none">{ic("stop",12,"#fff")}Стоп для всех</a></div>')
write("Motor.dc.html", page("Мотор для всех — VideoRecorder", mo))

# =====================================================================
#                 СТРАНИЦА 2 — ПЛАГИН «ЧЕРТЕЖИ БАЗ»
# =====================================================================
import math
HOLO_OK=("#5fb4ff","rgba(95,180,255,0.16)")
HOLO_BAD=("#ff4d62","rgba(255,77,98,0.16)")

def project(x,y,z, cx=640, horizon=300, f=760, camh=3.2):
    return (cx + f*x/z, horizon + f*(camh-y)/z)

def hologram(yaw_deg, cx_world=0.0, z0=14.0, colors=HOLO_OK, walls=True):
    stroke,fill=colors
    cells=[(0,0),(1,0),(2,0),(3,0),(0,1),(1,1),(2,1),(3,1),(0,2),(1,2)]
    tris=[(2,2)]
    S=3.0; ox=-2.0*S; oz=-1.5*S
    a=math.radians(yaw_deg); ca,sa=math.cos(a),math.sin(a)
    def w(px,pz,py=0.0):
        rx=px*ca - pz*sa; rz=px*sa + pz*ca
        return project(rx+cx_world, py, rz+z0)
    polys=[]
    for (c,r) in cells:
        x0=ox+c*S; z1=oz+r*S
        pts=[w(x0,z1),w(x0+S,z1),w(x0+S,z1+S),w(x0,z1+S)]
        polys.append(f'<polygon points="{" ".join(f"{p[0]:.1f},{p[1]:.1f}" for p in pts)}" fill="{fill}" stroke="{stroke}" stroke-width="1.6"></polygon>')
    for (c,r) in tris:
        x0=ox+c*S; z1=oz+r*S
        pts=[w(x0,z1),w(x0+S,z1),w(x0,z1+S)]
        polys.append(f'<polygon points="{" ".join(f"{p[0]:.1f},{p[1]:.1f}" for p in pts)}" fill="{fill}" stroke="{stroke}" stroke-width="1.6"></polygon>')
    wallsvg=[]
    if walls:
        occ=set(cells)
        edges=[]
        for (c,r) in cells:
            for dc,dr,e in [(0,-1,"n"),(0,1,"s"),(-1,0,"w"),(1,0,"e")]:
                if (c+dc,r+dr) not in occ:
                    x0=ox+c*S; z1=oz+r*S
                    if e=="n": edges.append(((x0,z1),(x0+S,z1)))
                    if e=="s": edges.append(((x0,z1+S),(x0+S,z1+S)))
                    if e=="w": edges.append(((x0,z1),(x0,z1+S)))
                    if e=="e": edges.append(((x0+S,z1),(x0+S,z1+S)))
        for (p1,p2) in edges:
            pts=[w(p1[0],p1[1],0),w(p2[0],p2[1],0),w(p2[0],p2[1],3),w(p1[0],p1[1],3)]
            wallsvg.append(f'<polygon points="{" ".join(f"{p[0]:.1f},{p[1]:.1f}" for p in pts)}" fill="{fill.replace("0.16","0.07")}" stroke="{stroke}" stroke-width="1.2" stroke-opacity="0.8"></polygon>')
    # стрелка «перед базы»
    a1=w(0,oz-1.0,0.2); a2=w(0,oz-6.0,0.2)
    arrow=f'<line x1="{a1[0]:.1f}" y1="{a1[1]:.1f}" x2="{a2[0]:.1f}" y2="{a2[1]:.1f}" stroke="#ffffff" stroke-width="2.5" stroke-linecap="round"></line><circle cx="{a2[0]:.1f}" cy="{a2[1]:.1f}" r="5" fill="#ffffff"></circle>'
    anc=w(0,0,0)
    anchor=f'<circle cx="{anc[0]:.1f}" cy="{anc[1]:.1f}" r="6" fill="{stroke}" stroke="#fff" stroke-width="2"></circle>'
    return f'<svg width="1280" height="720" viewBox="0 0 1280 720" aria-hidden="true" style="position: absolute; inset: 0; pointer-events: none">{"".join(polys)}{"".join(wallsvg)}{arrow}{anchor}</svg>'

def footprint(cells, col=ROSE, n=6):
    out=""
    for r in range(n):
        for c in range(n):
            on=(c,r) in cells
            out+=f'<span style="display: block; border-radius: 2px; background: {col if on else "rgba(255,255,255,0.05)"}; opacity: {0.9 if on else 1}"></span>'
    return f'<div aria-hidden="true" style="width: 54px; height: 54px; display: grid; grid-template-columns: repeat({n}, minmax(0, 1fr)); gap: 2px; padding: 4px; box-sizing: border-box; border-radius: 10px; background: rgba(0,0,0,0.3); flex: none">{out}</div>'

bps=[("Замок на скале","142 блока · 18 предметов · 15×12 м","kio",7,[(1,1),(2,1),(3,1),(4,1),(1,2),(2,2),(3,2),(4,2),(1,3),(2,3),(3,3),(4,3),(2,4),(3,4)],True),
     ("Бункер 2×2","40 блоков · 6 предметов · 6×6 м","kio",3,[(2,2),(3,2),(2,3),(3,3)],False),
     ("Клан-база Frog","388 блоков · 41 предмет · 21×18 м","Andrey_RU",12,[(0,0),(1,0),(2,0),(3,0),(4,0),(5,0),(0,1),(5,1),(0,2),(2,2),(3,2),(5,2),(0,3),(5,3),(0,4),(1,4),(4,4),(5,4)],False),
     ("Башня снайпера","64 блока · 4 предмета · 6×6 м","kio",0,[(2,1),(3,1),(2,2),(3,2),(2,3),(3,3),(2,4),(3,4)],False)]

def bp_card(n,meta,author,placed,cells,sel):
    hl="background: rgba(163,22,47,0.16); border: 1px solid rgba(240,166,177,0.28)" if sel else f"background: {SURF}; border: 1px solid {LINE}"
    au=f' · {author}' if author!="kio" else ""
    return (f'<div style="display: flex; align-items: center; gap: 12px; padding: 8px 10px 8px 8px; border-radius: 16px; {hl}">'
        f'{footprint(cells)}'
        f'<div style="display: flex; flex-direction: column; gap: 4px; flex: 1 1 auto; min-width: 0"><span style="font-size: 15px; font-weight: 700; color: {TEXT}">{n}</span><span style="font-size: 11px; color: {DIM}">{meta}{au}</span>'
        f'<div style="display: flex; gap: 4px">{chip(f"поставлена ×{placed}","layers")}</div></div>'
        f'<a href="BpPlace.dc.html" style="height: 38px; border-radius: 19px; background: {ACC}; color: #fff; display: flex; align-items: center; gap: 8px; padding: 0 16px; font-size: 13px; font-weight: 700; text-decoration: none; white-space: nowrap">{ic("pin",15,"#fff")}Выбрать место</a>'
        f'{iconbtn("more","Переименовать, поделиться, удалить",34,"transparent",DIM,10)}</div>')

def bp_toggle(t,on,icn):
    return f'<div style="display: flex; align-items: center; gap: 8px; height: 40px; padding: 0 10px; border-radius: 12px; background: {SURF}; border: 1px solid {LINE}; flex: 1 1 0; min-width: 0"><span style="color: {DIM}; display: flex">{ic(icn,14)}</span><span style="font-size: 12px; color: {TEXT}; flex: 1 1 auto; white-space: nowrap">{t}</span>{switch(on,t)}</div>'

lib=(f'<div style="position: absolute; inset: 0; background: rgba(8,6,6,0.35)"></div>'
 f'<section aria-label="Чертежи баз" style="position: absolute; left: 50%; top: 50%; transform: translate(-50%, -50%); width: 640px; box-sizing: border-box; border-radius: 24px; {GLASS}; padding: 18px; display: flex; flex-direction: column; gap: 14px; {FONT}">'
 f'<header style="display: flex; align-items: center; gap: 12px"><div style="width: 42px; height: 42px; border-radius: 13px; background: {ACC}; display: flex; align-items: center; justify-content: center; flex: none">{ic("layers",20,"#fff")}</div>'
 f'<div style="display: flex; flex-direction: column; gap: 1px; flex: 1 1 auto"><span style="font-size: 19px; font-weight: 700">Чертежи баз</span><span style="font-size: 12px; color: {DIM}">построил один раз — ставь где угодно</span></div>'
 f'{btn("Отменить постановку","undo","ghost",34,fs=12)}{iconbtn("x","Закрыть",34,SURF,TEXT,17)}</header>'
 f'<div style="border-radius: 16px; background: {SURF}; border: 1px solid {LINE}; padding: 12px; display: flex; flex-direction: column; gap: 8px">{label("Новый чертёж")}'
 f'<div style="display: flex; gap: 8px">{inp("название: замок, бункер, клан-база…","","tag")}<a href="BpSave.dc.html" style="height: 38px; border-radius: 19px; background: {ACC}; color: #fff; display: flex; align-items: center; gap: 8px; padding: 0 16px; font-size: 13px; font-weight: 700; text-decoration: none; white-space: nowrap">{ic("crosshair",15,"#fff")}Сохранить базу под прицелом</a></div>'
 f'<span style="font-size: 11px; color: {DIM}">Наведи прицел на любой блок: сохранится вся постройка с мебелью и ящиками.</span></div>'
 f'<div style="display: flex; align-items: center; justify-content: space-between">{label("Чертежи · 4")}<div style="display: flex; gap: 2px; padding: 2px; border-radius: 10px; background: {SURF}"><span style="height: 26px; padding: 0 12px; border-radius: 8px; background: {ACC}; color: #fff; font-size: 11px; font-weight: 700; display: flex; align-items: center">Все</span><span style="height: 26px; padding: 0 12px; border-radius: 8px; color: {DIM}; font-size: 11px; font-weight: 700; display: flex; align-items: center">Мои</span></div></div>'
 f'<div style="display: flex; flex-direction: column; gap: 6px">{"".join(bp_card(*b) for b in bps)}</div>'
 f'<div style="display: flex; flex-direction: column; gap: 6px">{label("При постановке")}'
 f'<div style="display: flex; gap: 6px">{bp_toggle("Мебель и ящики",True,"bag")}{bp_toggle("Лут в ящиках",False,"ammo")}{bp_toggle("Авторизация",True,"shield")}</div>'
 f'<div style="display: flex; gap: 6px">{bp_toggle("Стабильность",False,"tank")}{bp_toggle("Автовысота",True,"move")}<div style="flex: 1 1 0; display: flex; align-items: center; gap: 6px; font-size: 11px; color: {DIM}">{kbd("E")}ставить {kbd("R")}крутить</div></div></div>'
 '</section>')
write("BpLibrary.dc.html", page("Чертежи баз — библиотека", lib))

def place_hud(ok, reason=None):
    bar="#5fb4ff" if ok else "#ff4d62"
    status=(f'<span style="display: flex; align-items: center; gap: 6px; color: #8fcaff; font-weight: 700">{ic("check",14,"#8fcaff")}Можно ставить</span>' if ok else
            f'<span style="display: flex; align-items: center; gap: 6px; color: #ff8a98; font-weight: 700">{ic("x",14,"#ff8a98")}Нельзя: {reason}</span>')
    return (f'<div style="position: absolute; left: 50%; top: 20px; transform: translateX(-50%); width: 560px; box-sizing: border-box; border-radius: 20px; {GLASS}; overflow: hidden; {FONT}">'
        f'<div style="height: 4px; background: {bar}"></div>'
        f'<div style="padding: 12px 16px; display: flex; flex-direction: column; gap: 8px">'
        f'<div style="display: flex; align-items: center; gap: 10px"><span style="color: {ROSE}; display: flex">{ic("layers",16)}</span><span style="font-size: 16px; font-weight: 700; flex: 1 1 auto">Замок на скале</span>{chip("поворот 20°","rotate",TEXT)}{chip("высота +0.5 м","move",TEXT)}{chip("автовысота","check",TEXT)}</div>'
        f'<div style="display: flex; align-items: center; justify-content: space-between; font-size: 13px">{status}<span style="font-size: 11px; color: {DIM}">142 блока · 18 предметов</span></div>'
        '</div></div>')

def keys_bar(force=False):
    items=[("E","поставить"),("R","повернуть +15°"),("Shift+R","−15°"),("PgUp/PgDn","высота"),("ПКМ","выйти")]
    if force: items.insert(1,("Shift+E","поставить всё равно"))
    k="".join(f'<span style="display: flex; align-items: center; gap: 6px">{kbd(a)}{b}</span>' for a,b in items)
    return f'<div style="position: absolute; left: 50%; bottom: 24px; transform: translateX(-50%); display: flex; align-items: center; gap: 16px; height: 42px; padding: 0 18px; border-radius: 21px; {GLASS}; {FONT}; font-size: 12px; color: {DIM}; white-space: nowrap">{k}</div>'

toast=(f'<div style="position: absolute; right: 20px; bottom: 84px; width: 300px; box-sizing: border-box; border-radius: 16px; {GLASS}; padding: 12px 14px; display: flex; align-items: center; gap: 10px; {FONT}">'
  f'<span style="width: 30px; height: 30px; border-radius: 15px; background: rgba(127,209,139,0.2); color: #a6e3b0; display: flex; align-items: center; justify-content: center; flex: none">{ic("check",15)}</span>'
  f'<div style="display: flex; flex-direction: column; flex: 1 1 auto"><span style="font-size: 13px; font-weight: 700">База поставлена</span><span style="font-size: 11px; color: {DIM}">160 объектов · можно ставить следующую</span></div>'
  f'{btn("Отменить",None,"ghost",30,fs=11)}</div>')

place=hologram(20, 0.0, 14.0, HOLO_OK)+place_hud(True)+toast+keys_bar()
write("BpPlace.dc.html", page("Постановка базы — можно", place))

blocked=(hologram(-10, 1.5, 12.5, HOLO_BAD)
  + f'<div style="position: absolute; left: 700px; top: 300px; display: flex; align-items: center; gap: 6px; height: 26px; padding: 0 10px; border-radius: 13px; background: rgba(255,77,98,0.9); color: #fff; font-size: 11px; font-weight: 700; {FONT}">{ic("x",12,"#fff")}тут чужой фундамент</div>'
  + place_hud(False,"мешает другая постройка") + keys_bar(True))
write("BpBlocked.dc.html", page("Постановка базы — нельзя", blocked))

save=(f'<div style="position: absolute; left: 50%; top: 50%; width: 22px; height: 22px; margin: -11px 0 0 -11px; border-radius: 11px; border: 2px solid #fff; box-sizing: border-box"></div>'
  + f'<div style="position: absolute; left: 50%; top: 50%; width: 4px; height: 4px; margin: -2px 0 0 -2px; border-radius: 2px; background: #fff"></div>'
  + f'<div style="position: absolute; left: 50%; top: 50%; transform: translate(24px, -50%); display: flex; align-items: center; gap: 8px; height: 30px; padding: 0 12px; border-radius: 15px; {GLASS}; font-size: 12px; font-weight: 700; {FONT}">{ic("crosshair",13,ROSE)}фундамент · постройка kio · 142 блока</div>'
  + f'<div style="position: absolute; left: 50%; top: 20px; transform: translateX(-50%); width: 460px; box-sizing: border-box; border-radius: 20px; {GLASS}; padding: 14px 16px; display: flex; flex-direction: column; gap: 10px; {FONT}">'
  + f'<div style="display: flex; align-items: center; gap: 10px"><span style="width: 32px; height: 32px; border-radius: 16px; background: {ACC}; display: flex; align-items: center; justify-content: center; flex: none">{ic("import",15,"#fff")}</span><div style="display: flex; flex-direction: column; flex: 1 1 auto"><span style="font-size: 15px; font-weight: 700">Сохраняю «Замок на скале»…</span><span style="font-size: 11px; color: {DIM}">не отводи прицел, пока не появится галочка</span></div></div>'
  + f'<div style="height: 6px; border-radius: 3px; background: rgba(255,255,255,0.1)"><div style="width: 64%; height: 6px; border-radius: 3px; background: {ACC}"></div></div>'
  + f'<div style="display: flex; gap: 6px">{chip("142 блока","layers",TEXT)}{chip("18 предметов","bag",TEXT)}{chip("15×12 м","move",TEXT)}</div></div>'
  + f'<a href="BpLibrary.dc.html" style="position: absolute; left: 50%; bottom: 24px; transform: translateX(-50%); display: flex; align-items: center; gap: 8px; height: 40px; padding: 0 16px; border-radius: 20px; {GLASS}; {FONT}; font-size: 12px; color: {TEXT}; text-decoration: none">{ic("left",14)}Назад в библиотеку {kbd("/bp")}</a>')
write("BpSave.dc.html", page("Сохранение чертежа", save))

# ================= Blueprints 1.1 — обновлённая библиотека и массив
def bp_card2(n,meta,author,placed,cells,fav,public,mine,cost=None):
    hl=f"background: {SURF}; border: 1px solid {LINE}"
    au=f' · {author}' if author!="kio" else ""
    pub=f'<span style="height: 18px; padding: 0 7px; border-radius: 9px; background: rgba(240,166,177,0.16); color: {ROSE}; font-size: 9px; font-weight: 700; letter-spacing: 0.6px; display: inline-flex; align-items: center">ПУБЛИЧНЫЙ</span>' if public else ""
    star=f'<button type="button" aria-label="В избранное" style="width: 30px; height: 30px; border: none; border-radius: 15px; background: {"rgba(163,22,47,0.3)" if fav else "transparent"}; color: {ROSE if fav else DIM}; display: flex; align-items: center; justify-content: center; cursor: pointer; flex: none"><svg width="15" height="15" viewBox="0 0 24 24" aria-hidden="true" fill="{"currentColor" if fav else "none"}" stroke="currentColor" stroke-width="2" stroke-linejoin="round"><path d="M12 3l2.7 5.6 6.1.9-4.4 4.3 1 6.1L12 17l-5.4 2.9 1-6.1L3.2 9.5l6.1-.9z"></path></svg></button>'
    costline=f'<span style="font-size: 11px; color: {ROSE}; display: flex; align-items: center; gap: 4px">{ic("tag",11)}{cost}</span>' if cost else ""
    mgmt=(iconbtn("edit","Переименовать",30,"transparent",DIM,15)+iconbtn("x","Удалить",30,"transparent",DIM,15)) if mine else ""
    return (f'<div style="display: flex; align-items: center; gap: 12px; padding: 8px 10px 8px 8px; border-radius: 16px; {hl}">'
        f'{footprint(cells)}'
        f'<div style="display: flex; flex-direction: column; gap: 3px; flex: 1 1 auto; min-width: 0"><div style="display: flex; align-items: center; gap: 6px"><span style="font-size: 15px; font-weight: 700; color: {TEXT}">{n}</span>{pub}</div><span style="font-size: 11px; color: {DIM}">{meta} · ставили ×{placed}{au}</span>{costline}</div>'
        f'{star}<button type="button" style="height: 32px; border: none; border-radius: 16px; background: {SURF}; color: {TEXT}; display: flex; align-items: center; gap: 6px; padding: 0 12px; font-size: 11px; font-weight: 600; cursor: pointer; {FONT}; white-space: nowrap">{ic("pin",13)}На место</button>'
        f'<a href="BpArray.dc.html" style="height: 36px; border-radius: 18px; background: {ACC}; color: #fff; display: flex; align-items: center; gap: 8px; padding: 0 14px; font-size: 13px; font-weight: 700; text-decoration: none; white-space: nowrap">{ic("crosshair",14,"#fff")}Выбрать место</a>{mgmt}</div>')

def seg2(opts,active,h=30):
    o=""
    for x in opts:
        a=x==active
        st=f"background: {ACC}; color: #fff" if a else f"background: transparent; color: {DIM}"
        o+=f'<button type="button" style="flex: 1 1 0; min-width: 0; height: {h-4}px; border: none; border-radius: 9px; {st}; font-size: 11px; font-weight: 700; cursor: pointer; {FONT}; white-space: nowrap">{x}</button>'
    return f'<div style="display: flex; gap: 2px; padding: 2px; border-radius: 11px; background: rgba(0,0,0,0.28); flex: 1 1 auto">{o}</div>'

cards2="".join([
 bp_card2("Бункер 2×2","40 блоков · 6 предметов · 6×6 м","kio",3,[(2,2),(3,2),(2,3),(3,3)],True,False,True,"8 000 металла ×3"),
 bp_card2("Замок на скале","142 блока · 18 предметов · 15×12 м","kio",7,[(1,1),(2,1),(3,1),(4,1),(1,2),(2,2),(3,2),(4,2),(1,3),(2,3),(3,3),(4,3),(2,4),(3,4)],False,True,True,None),
 bp_card2("Клан-база Frog","388 блоков · 41 предмет · 21×18 м","Andrey_RU",12,[(0,0),(1,0),(2,0),(3,0),(4,0),(5,0),(0,1),(5,1),(0,2),(2,2),(3,2),(5,2),(0,3),(5,3),(0,4),(1,4),(4,4),(5,4)],False,True,False,None),
])
lib2=(f'<div style="position: absolute; inset: 0; background: rgba(8,6,6,0.35)"></div>'
 f'<section aria-label="Чертежи баз" style="position: absolute; left: 50%; top: 50%; transform: translate(-50%, -50%); width: 720px; box-sizing: border-box; border-radius: 24px; {GLASS}; padding: 18px; display: flex; flex-direction: column; gap: 12px; {FONT}">'
 f'<header style="display: flex; align-items: center; gap: 12px"><div style="width: 42px; height: 42px; border-radius: 13px; background: {ACC}; display: flex; align-items: center; justify-content: center; flex: none">{ic("layers",20,"#fff")}</div>'
 f'<div style="display: flex; flex-direction: column; gap: 1px; flex: 1 1 auto"><span style="font-size: 19px; font-weight: 700">Чертежи баз</span><span style="font-size: 12px; color: {DIM}">твоих баз на карте: 4 · лимит 10</span></div>'
 f'{btn("Отменить","undo","ghost",34,fs=12)}{btn("Убрать все мои","x","ghost",34,fs=12)}{iconbtn("x","Закрыть",34,SURF,TEXT,17)}</header>'
 f'<div style="display: flex; gap: 8px">{inp("название нового чертежа или новое имя…","","tag")}<a href="BpSave.dc.html" style="height: 38px; border-radius: 19px; background: {ACC}; color: #fff; display: flex; align-items: center; gap: 8px; padding: 0 16px; font-size: 13px; font-weight: 700; text-decoration: none; white-space: nowrap">{ic("crosshair",15,"#fff")}Сохранить базу под прицелом</a></div>'
 f'<div style="display: flex; gap: 8px; align-items: center">{inp("поиск по названию или автору…","","eye")}<div style="width: 300px; flex: none; display: flex">{seg2(["Все","Мои","Избранное"],"Все",34)}</div></div>'
 f'<div style="display: flex; flex-direction: column; gap: 6px">{cards2}</div>'
 f'<div style="border-radius: 16px; background: {SURF}; border: 1px solid {LINE}; padding: 12px; display: flex; flex-direction: column; gap: 10px">{label("При постановке")}'
 f'<div style="display: flex; align-items: center; gap: 10px"><span style="width: 90px; font-size: 11px; color: {DIM}; font-weight: 600">МАТЕРИАЛ</span>{seg2(["Как есть","Солома","Дерево","Камень","Металл","МВК"],"Металл")}</div>'
 f'<div style="display: flex; align-items: center; gap: 10px"><span style="width: 90px; font-size: 11px; color: {DIM}; font-weight: 600">КОПИЙ РЯДОМ</span>{stepper("×3")}<span style="width: 16px"></span><span style="font-size: 11px; color: {DIM}; font-weight: 600">ШАГ ПОВОРОТА</span><div style="width: 180px; display: flex">{seg2(["15°","45°","90°"],"90°")}</div></div>'
 f'<div style="display: flex; gap: 6px">{bp_toggle("Мебель",True,"bag")}{bp_toggle("Лут",False,"ammo")}{bp_toggle("Шкаф",True,"shield")}{bp_toggle("Стабильн.",False,"tank")}{bp_toggle("Автовыс.",True,"move")}</div>'
 '</div></section>')
write("BpLibrary.dc.html", page("Чертежи баз — библиотека", lib2))

# массив из 3 копий, металл
def hologram_multi(yaw, xs, z0, colors):
    svgs=[hologram(yaw, x, z0, colors, True) for x in xs]
    inner="".join(s[s.index(">")+1:s.rindex("</svg>")] for s in svgs)
    return f'<svg width="1280" height="720" viewBox="0 0 1280 720" aria-hidden="true" style="position: absolute; inset: 0; pointer-events: none">{inner}</svg>'
arr_hud=(f'<div style="position: absolute; left: 50%; top: 20px; transform: translateX(-50%); width: 620px; box-sizing: border-box; border-radius: 20px; {GLASS}; overflow: hidden; {FONT}">'
 f'<div style="height: 4px; background: #5fb4ff"></div><div style="padding: 12px 16px; display: flex; flex-direction: column; gap: 8px">'
 f'<div style="display: flex; align-items: center; gap: 8px"><span style="color: {ROSE}; display: flex">{ic("layers",16)}</span><span style="font-size: 16px; font-weight: 700; flex: 1 1 auto">Бункер 2×2</span>{chip("90° · шаг 90°","rotate",TEXT)}{chip("металл","shield",TEXT)}{chip("×3 копии","copy",TEXT)}{chip("автовысота","move",TEXT)}</div>'
 f'<div style="display: flex; align-items: center; justify-content: space-between; font-size: 13px"><span style="display: flex; align-items: center; gap: 6px; color: #8fcaff; font-weight: 700">{ic("check",14,"#8fcaff")}Можно ставить все 3</span><span style="font-size: 12px; color: {ROSE}; display: flex; align-items: center; gap: 4px">{ic("tag",12)}Цена: 24 000 металла · у тебя 31 500</span></div>'
 '</div></div>')
num_labels=""
for i,x in enumerate([-13.5,0,13.5]):
    p=project(x,3.6,26.0)
    num_labels+=f'<span style="position: absolute; left: {p[0]-12:.0f}px; top: {p[1]-12:.0f}px; width: 24px; height: 24px; border-radius: 12px; background: rgba(18,14,14,0.8); border: 1px solid #5fb4ff; color: #fff; font-size: 12px; font-weight: 700; display: flex; align-items: center; justify-content: center; {FONT}">{i+1}</span>'
arr=hologram_multi(0,[-13.5,0,13.5],26.0,HOLO_OK)+num_labels+arr_hud+keys_bar()
write("BpArray.dc.html", page("Постановка массивом ×3 — металл", arr))

# ================= Weather
presets=[("sun","Ясный полдень","12:00",False),("sun","Золотой час","18:30",False),("fog","Рассвет в тумане","06:00",False),("cloud","Пасмурно","13:00",False),("rain","Ливень","15:00",False),
         ("bolt","Гроза ночью","21:00",False),("smoke","Метель","14:00",True),("fog","Туман-хоррор","23:00",False),("sun","Ночь при луне","00:00",False),("flare","Радуга","16:00",False)]
pg=""
for icn,n,t,a in presets:
    st=f"background: rgba(163,22,47,0.25); border: 1px solid rgba(240,166,177,0.5); color: #fff" if a else f"background: {SURF}; border: 1px solid {LINE}; color: {TEXT}"
    pg+=f'<button type="button" style="height: 54px; border-radius: 12px; {st}; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 2px; cursor: pointer; {FONT}; padding: 0 2px">{ic(icn,15)}<span style="font-size: 10px; font-weight: 700; text-align: center; line-height: 1.1">{n}</span><span style="font-size: 9px; color: {DIM}">{t}</span></button>'
def wrow(n,icn,val,auto=False):
    return (f'<div style="display: flex; align-items: center; gap: 8px; height: 26px"><span style="color: {DIM}; display: flex">{ic(icn,13)}</span><span style="font-size: 12px; width: 74px; flex: none">{n}</span>'
            + (f'<span style="flex: 1 1 auto; font-size: 11px; color: {DIM}">авто (как на сервере)</span>' if auto else slider(val, f"{val}%"))
            + f'<button type="button" style="height: 22px; padding: 0 8px; border: none; border-radius: 11px; background: {ACC if auto else SURF}; color: {"#fff" if auto else DIM}; font-size: 10px; font-weight: 700; cursor: pointer; {FONT}">АВТО</button></div>')
wc=(f'<div style="display: flex; align-items: center; gap: 8px"><div style="display: flex; align-items: center; gap: 8px; flex: 1 1 auto">{switch(True,"Погода сцены")}<span style="font-size: 13px; font-weight: 600">Погода сцены</span></div>{btn("Применить","check","primary",32,fs=12)}{btn("Вернуть серверную","undo","ghost",32,fs=12)}</div>'
 + f'<div style="display: grid; grid-template-columns: repeat(5, minmax(0, 1fr)); gap: 5px">{pg}</div>'
 + card(f'<div style="display: flex; align-items: center; gap: 10px"><span style="color: {ROSE}; display: flex">{ic("sun",15)}</span>{slider(58,"14:00")}</div>'
        + f'<div style="display: flex; gap: 6px">{seg(["Только команда","Весь сервер"],"Только команда").replace("min-width: 44px","flex: 1 1 0")}{seg(["Время стоит","Время идёт"],"Время стоит").replace("min-width: 44px","flex: 1 1 0")}</div>',"Время суток",10,8)
 + card(f'<div style="display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); column-gap: 14px; row-gap: 4px">'
        + wrow("Облака","cloud",100)+wrow("Туман","fog",60)+wrow("Дождь/снег","rain",100)+wrow("Ветер","smoke",100)
        + wrow("Гроза","bolt",0,True)+wrow("Радуга","flare",0,True)+wrow("Яркость","sun",40)+wrow("Контраст","sliders",40) + '</div>',"Тонкая настройка · пресет «Метель»",10,6))
write("Weather.dc.html", page("Погода и время — VideoRecorder", panel("Weather.dc.html",wc,False,8)+dock()))
