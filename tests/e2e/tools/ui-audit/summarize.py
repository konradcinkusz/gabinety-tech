#!/usr/bin/env python3
"""Prints one line per page visit from docs/ui/audit/findings.json: the mechanical findings only. Usage: summarize.py [findings.json]"""
import json, sys
d = json.load(open(sys.argv[1] if len(sys.argv) > 1 else 'findings.json'))
print({k: d[k] for k in ['guards', 'deadLinks', 'deadButtons']})
for f in d['findings']:
    iss = []
    if f.get('error'): iss.append('ERR ' + f['error'])
    if f.get('hscroll'): iss.append('HSCROLL %s' % f['hscroll'])
    if f.get('offscreen'): iss.append('off:%s' % f['offscreen'][:2])
    if f.get('clipped'): iss.append('clip:%s' % f['clipped'][:2])
    if f.get('overlaps'): iss.append('ovl:%s' % f['overlaps'][:2])
    if f['vp'] == 390 and f.get('tapTotal'): iss.append('tap:%d' % f['tapTotal'])
    if f.get('imgNoAlt'): iss.append('noalt')
    if f.get('consoleErrors'): iss.append('con:%s' % [c[:60] for c in f['consoleErrors'][:2]])
    if f.get('failedRequests'): iss.append('fail:%s' % f['failedRequests'][:2])
    if f.get('badResponses'): iss.append('bad:%s' % f['badResponses'][:2])
    if f.get('cls', 0) > 0.1: iss.append('cls %.2f' % f['cls'])
    if f['focus']['missingCount']: iss.append('focus:%s' % f['focus']['missing'][:2])
    ax = [(a['id'], a['count']) for a in f['axe']]
    if ax: iss.append('axe:%s' % ax)
    if f.get('mainTextLength', 999) < 150: iss.append('short main %d' % f['mainTextLength'])
    if f.get('formControlsNoLabel'): iss.append('nolabel:%s' % f['formControlsNoLabel'][:2])
    if f['finalUrl'] != f['path']: iss.append('->%s' % f['finalUrl'])
    print(f['role'], f['vp'], f['scheme'], f['route'], f['status'], '|', f['title'][:30], '|', ' ; '.join(iss))
