const PredictionsTab = (() => {
  let _matches    = null;  // upcoming matches
  let _myPreds    = {};    // matchId → prediction object

  // ── Helpers ────────────────────────────────────────────────────────────────

  function isOpen(match) {
    return new Date() < new Date(match.deadlineUtc);
  }

  function minsLeft(match) {
    return Math.floor((new Date(match.deadlineUtc) - new Date()) / 60000);
  }

  function fmtDeadline(match) {
    const d = new Date(match.deadlineUtc);
    return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) +
           ' ' + d.toLocaleDateString(i18n.lang === 'ru' ? 'ru-RU' : 'en-GB',
             { day: 'numeric', month: 'short' });
  }

  function emblemHtml(team) {
    if (team.emblemUrl)
      return `<img class="pred-emblem" src="${team.emblemUrl}" alt="${escHtml(team.name)}"
               onerror="this.onerror=null;this.src=''">`;
    return `<div class="match-emblem-ph" style="width:28px;height:28px;font-size:9px">
              ${(team.name||'?').substring(0,3).toUpperCase()}</div>`;
  }

  function ptsCls(pts) {
    if (pts === null || pts === undefined) return '';
    if (pts >= 3) return 'exact';
    if (pts === 1) return 'outcome';
    return 'miss';
  }

  function ptsLabel(pts) {
    if (pts >= 3) return i18n.t('pred.exact');
    if (pts === 1) return i18n.t('pred.outcome');
    return i18n.t('pred.miss');
  }

  // ── Render one card ────────────────────────────────────────────────────────

  function renderCard(match) {
    const pred   = _myPreds[match.matchId];
    const open   = isOpen(match);
    const scored = pred && pred.isScored;
    const mins   = minsLeft(match);
    const urgent = open && mins <= 60;

    // Input state
    let homeVal = pred ? pred.predictedHome : 0;
    let awayVal = pred ? pred.predictedAway : 0;

    const deadlineHtml = open
      ? `<div class="pred-deadline${urgent ? ' urgent' : ''}">
           ${i18n.t('pred.deadline', { d: fmtDeadline(match) })}
         </div>`
      : `<div class="pred-deadline">${i18n.t('pred.closed')}</div>`;

    const scoreInputOrClosed = open
      ? `<div class="score-input-row" id="sir-${match.matchId}">
           <div class="score-input-group">
             <button class="score-btn" onclick="PredictionsTab.adjust(${match.matchId},'home',-1)" id="hminus-${match.matchId}">−</button>
             <div class="score-display" id="hval-${match.matchId}">${homeVal}</div>
             <button class="score-btn" onclick="PredictionsTab.adjust(${match.matchId},'home',+1)">+</button>
           </div>
           <div class="score-separator">:</div>
           <div class="score-input-group">
             <button class="score-btn" onclick="PredictionsTab.adjust(${match.matchId},'away',-1)" id="aminus-${match.matchId}">−</button>
             <div class="score-display" id="aval-${match.matchId}">${awayVal}</div>
             <button class="score-btn" onclick="PredictionsTab.adjust(${match.matchId},'away',+1)">+</button>
           </div>
         </div>
         <button class="save-btn${pred ? ' saved' : ''}" id="savebtn-${match.matchId}"
                 onclick="PredictionsTab.save(${match.matchId})">
           ${pred ? i18n.t('pred.saved') : i18n.t('pred.save')}
         </button>`
      : `<div class="pred-closed">🔒 ${i18n.t('pred.closed')}</div>`;

    const scoredHtml = scored
      ? `<div class="scored-row">
           <span class="scored-my">${i18n.t('profile.you')}: <b>${pred.predictedHome}–${pred.predictedAway}</b></span>
           <span class="scored-actual">${i18n.t('profile.actual')}: <b>${pred.actualHome}–${pred.actualAway}</b></span>
           <span class="scored-pts ${ptsCls(pred.pointsAwarded)}">
             +${pred.pointsAwarded} · ${ptsLabel(pred.pointsAwarded)}
           </span>
         </div>`
      : '';

    return `<div class="prediction-card" id="pcard-${match.matchId}">
      <div class="pred-match-header">
        <div class="pred-team">${emblemHtml(match.homeTeam)}<span>${escHtml(match.homeTeam.name)}</span></div>
        <div class="pred-vs">vs</div>
        <div class="pred-team right"><span>${escHtml(match.awayTeam.name)}</span>${emblemHtml(match.awayTeam)}</div>
      </div>
      ${deadlineHtml}
      ${scoreInputOrClosed}
      ${scoredHtml}
    </div>`;
  }

  // ── Adjust score ──────────────────────────────────────────────────────────

  function adjust(matchId, side, delta) {
    const id  = side === 'home' ? `hval-${matchId}` : `aval-${matchId}`;
    const el  = document.getElementById(id);
    let   val = parseInt(el.textContent) + delta;
    if (val < 0) val = 0;
    if (val > 20) val = 20;
    el.textContent = val;

    // Mark save button as unsaved
    const btn = document.getElementById(`savebtn-${matchId}`);
    if (btn) { btn.textContent = i18n.t('pred.save'); btn.classList.remove('saved'); }
  }

  // ── Save prediction ────────────────────────────────────────────────────────

  async function save(matchId) {
    const btn  = document.getElementById(`savebtn-${matchId}`);
    const home = parseInt(document.getElementById(`hval-${matchId}`).textContent);
    const away = parseInt(document.getElementById(`aval-${matchId}`).textContent);

    btn.disabled = true;
    btn.textContent = '…';
    try {
      const saved = await Api.savePred(matchId, home, away);
      _myPreds[matchId] = saved;
      btn.textContent = i18n.t('pred.saved');
      btn.classList.add('saved');
      window.Telegram?.WebApp?.HapticFeedback?.notificationOccurred('success');
    } catch(e) {
      btn.textContent = e.data?.error || 'Error';
      setTimeout(() => {
        btn.textContent = i18n.t('pred.save');
        btn.disabled = false;
      }, 2000);
      return;
    }
    btn.disabled = false;
  }

  // ── Load & render ─────────────────────────────────────────────────────────

  async function load() {
    const container = document.getElementById('predictions-body');
    container.innerHTML = '<div class="spinner"></div>';
    try {
      const [matches, preds] = await Promise.all([Api.upcoming(), Api.predictions()]);
      _matches = matches;
      _myPreds = {};
      preds.forEach(p => { _myPreds[p.matchId] = p; });
      render();
    } catch(e) {
      const msg = e.data?.error || e.message || 'Failed to load';
      container.innerHTML = `<div class="empty-state"><div class="empty-icon">⚠️</div><div class="empty-msg">${msg}</div></div>`;
    }
  }

  function render() {
    const container = document.getElementById('predictions-body');
    if (!_matches || _matches.length === 0) {
      container.innerHTML = `<div class="empty-state">
        <div class="empty-icon">🔮</div>
        <div class="empty-msg">${i18n.t('pred.noMatches')}</div>
      </div>`;
      return;
    }
    container.innerHTML = _matches.map(renderCard).join('');
  }

  return { load, adjust, save };
})();
