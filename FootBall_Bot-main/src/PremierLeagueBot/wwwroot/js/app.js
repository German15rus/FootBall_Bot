// ── Global helpers ──────────────────────────────────────────────────────────

function escHtml(str) {
  return String(str || '').replace(/&/g,'&amp;').replace(/</g,'&lt;')
    .replace(/>/g,'&gt;').replace(/"/g,'&quot;').replace(/'/g,'&#39;');
}

// ── Team Modal ──────────────────────────────────────────────────────────────

const TeamModal = (() => {
  let _activeTab = 'squad';

  function open(teamId, name, emblemUrl) {
    const modal = document.getElementById('team-modal');
    document.getElementById('modal-team-name').textContent = name;
    const emb = document.getElementById('modal-team-emblem');
    if (emblemUrl) { emb.src = emblemUrl; emb.style.display = 'block'; }
    else emb.style.display = 'none';

    document.getElementById('modal-squad').innerHTML  = '<div class="spinner"></div>';
    document.getElementById('modal-recent').innerHTML = '<div class="spinner"></div>';
    modal.classList.remove('hidden');

    // Lock body scroll
    document.querySelector('.app').style.overflow = 'hidden';

    Api.team(teamId).then(data => {
      renderSquad(data.squad);
      renderRecent(data.recentMatches, teamId);
    }).catch(() => {
      document.getElementById('modal-squad').innerHTML = '<div class="empty-state"><div class="empty-icon">⚠️</div></div>';
    });

    switchModalTab(_activeTab);
  }

  function close() {
    document.getElementById('team-modal').classList.add('hidden');
    document.querySelector('.app').style.overflow = '';
  }

  function renderSquad(squad) {
    if (!squad || squad.length === 0) {
      document.getElementById('modal-squad').innerHTML =
        '<div class="empty-state"><div class="empty-icon">👤</div><div class="empty-sub">No squad data</div></div>';
      return;
    }
    const posOrder = { GK: 0, GKP: 0, Goalkeeper: 0,
      D: 1, DEF: 1, Defender: 1, CB: 1, LB: 1, RB: 1,
      M: 2, MID: 2, Midfielder: 2,
      F: 3, FWD: 3, Forward: 3, Attacker: 3 };
    const sorted = [...squad].sort((a, b) => {
      const pa = posOrder[a.position] ?? 9;
      const pb = posOrder[b.position] ?? 9;
      return pa !== pb ? pa - pb : a.number - b.number;
    });
    document.getElementById('modal-squad').innerHTML = sorted.map(p =>
      `<div class="player-row">
        <div class="player-number">${p.number || '–'}</div>
        <div class="player-name">${escHtml(p.name)}</div>
        <div class="player-pos">${escHtml(p.position)}</div>
       </div>`
    ).join('');
  }

  function renderRecent(matches, teamId) {
    if (!matches || matches.length === 0) {
      document.getElementById('modal-recent').innerHTML =
        '<div class="empty-state"><div class="empty-icon">📅</div><div class="empty-sub">No recent matches</div></div>';
      return;
    }
    document.getElementById('modal-recent').innerHTML = matches.map(m => {
      const score = (m.homeScore !== null && m.homeScore !== undefined)
        ? `${m.homeScore}–${m.awayScore}` : '–';
      const res = m.result || '';
      const date = new Date(m.matchDate).toLocaleDateString([], { day:'numeric', month:'short' });
      const opponent = m.homeTeamId === teamId ? m.awayTeamName : m.homeTeamName;
      const venue    = m.homeTeamId === teamId ? '(H)' : '(A)';
      return `<div class="recent-row">
        <div class="recent-date">${date}</div>
        <div class="recent-teams">${escHtml(opponent)} <span style="color:var(--text-muted);font-size:10px">${venue}</span></div>
        <div class="recent-score">${score} ${res ? `<span class="result-badge ${res}">${res}</span>` : ''}</div>
      </div>`;
    }).join('');
  }

  function switchModalTab(tab) {
    _activeTab = tab;
    document.querySelectorAll('.modal-tab').forEach(btn => {
      btn.classList.toggle('active', btn.dataset.modalTab === tab);
    });
    document.getElementById('modal-squad').classList.toggle('hidden',  tab !== 'squad');
    document.getElementById('modal-recent').classList.toggle('hidden', tab !== 'recent');
  }

  return { open, close, switchModalTab };
})();

// ── Tab Router ──────────────────────────────────────────────────────────────

const Router = (() => {
  const tabs   = ['standings', 'matches', 'predictions', 'profile'];
  let _current = 'standings';
  let _loaded  = {};

  function go(tab) {
    if (!tabs.includes(tab)) return;
    _current = tab;

    document.querySelectorAll('.tab-content').forEach(el => el.classList.remove('active'));
    document.querySelectorAll('.nav-btn').forEach(btn => {
      btn.classList.toggle('active', btn.dataset.tab === tab);
    });
    document.getElementById(`tab-${tab}`).classList.add('active');

    if (!_loaded[tab]) {
      _loaded[tab] = true;
      loadTab(tab);
    }
  }

  function loadTab(tab) {
    if (tab === 'standings')   StandingsTab.load();
    if (tab === 'matches')     MatchesTab.load();
    if (tab === 'predictions') PredictionsTab.load();
    if (tab === 'profile')     ProfileTab.load();
  }

  return { go };
})();

// ── Boot ────────────────────────────────────────────────────────────────────

(async function boot() {
  const tg = window.Telegram?.WebApp;
  if (tg) {
    tg.ready();
    tg.expand();
    if (tg.colorScheme === 'dark') document.body.classList.add('tg-dark');
    tg.onEvent('themeChanged', () => {
      document.body.classList.toggle('tg-dark', tg.colorScheme === 'dark');
    });
  }

  // Init i18n early (reads Telegram language)
  i18n.init();

  const loadingText = document.getElementById('loading-text');
  if (loadingText) loadingText.textContent = i18n.lang === 'ru' ? 'Загрузка…' : 'Loading…';

  // Load cached session token first so API calls work immediately
  Api.loadSessionToken();
  const initData = tg?.initData || '';
  Api.setInitData(initData);

  // Wire up navigation
  document.querySelectorAll('.nav-btn').forEach(btn => {
    btn.addEventListener('click', () => Router.go(btn.dataset.tab));
  });

  // Wire up modal close
  document.getElementById('modal-close').addEventListener('click', TeamModal.close);
  document.getElementById('modal-backdrop').addEventListener('click', TeamModal.close);
  document.querySelectorAll('.modal-tab').forEach(btn => {
    btn.addEventListener('click', () => TeamModal.switchModalTab(btn.dataset.modalTab));
  });

  // Show app immediately — don't block on login
  document.getElementById('loading-screen').classList.add('hidden');
  document.getElementById('app').classList.remove('hidden');

  // Check for ?user= in URL for public profile viewing
  const params   = new URLSearchParams(window.location.search);
  const viewUser = params.get('user');

  if (viewUser) {
    Router.go('profile');
    ProfileTab.load(viewUser);
  } else {
    Router.go('standings');
  }

  // Login in background — registers user and refreshes session token
  if (initData) {
    fetch('/api/auth/login', {
      method:  'POST',
      headers: { 'Content-Type': 'application/json' },
      body:    JSON.stringify({ initData }),
      signal:  AbortSignal.timeout(8000),
    })
    .then(r => r.ok ? r.json() : null)
    .then(data => { if (data?.sessionToken) Api.setSessionToken(data.sessionToken); })
    .catch(e => console.warn('Background login failed:', e));
  }
})();
