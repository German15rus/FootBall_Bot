const MatchesTab = (() => {
  let _data = null;

  function formatTime(iso) {
    const d = new Date(iso);
    return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  }

  function formatDateLabel(iso) {
    const d = new Date(iso);
    return d.toLocaleDateString(i18n.lang === 'ru' ? 'ru-RU' : 'en-GB', {
      weekday: 'long', day: 'numeric', month: 'long'
    });
  }

  function teamHtml(team, side) {
    const ph = `<div class="match-emblem-ph">${(team.name||'?').substring(0,3).toUpperCase()}</div>`;
    const img = team.emblemUrl
      ? `<img class="match-emblem" src="${team.emblemUrl}" alt="${escHtml(team.name)}" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'">${ph}`
      : ph;
    const name = `<span>${escHtml(team.name)}</span>`;
    return side === 'left'
      ? `<div class="match-team">${img}${name}</div>`
      : `<div class="match-team right">${img}${name}</div>`;
  }

  function render(data) {
    const container = document.getElementById('matches-body');
    if (!data || data.length === 0) {
      container.innerHTML = `<div class="empty-state">
        <div class="empty-icon">📅</div>
        <div class="empty-msg">${i18n.lang === 'ru' ? 'Матчей нет' : 'No upcoming matches'}</div>
      </div>`;
      return;
    }

    // Group by date
    const groups = {};
    data.forEach(m => {
      const key = m.matchDate.substring(0, 10);
      if (!groups[key]) groups[key] = [];
      groups[key].push(m);
    });

    let html = '';
    for (const [date, matches] of Object.entries(groups)) {
      html += `<div class="date-group">
        <div class="date-label">${formatDateLabel(date)}</div>`;
      matches.forEach(m => {
        const isFinished = m.homeScore !== null && m.homeScore !== undefined;
        const center = isFinished
          ? `<div class="match-center">
               <div class="match-score">${m.homeScore}–${m.awayScore}</div>
             </div>`
          : `<div class="match-center">
               <div class="match-time">${formatTime(m.matchDate)}</div>
               ${m.stadium ? `<div class="match-stadium">${escHtml(m.stadium)}</div>` : ''}
             </div>`;

        html += `<div class="match-card" onclick="StandingsTab.openTeam(${m.homeTeam.id},'${escHtml(m.homeTeam.name)}','${escHtml(m.homeTeam.emblemUrl||'')}')">
          ${teamHtml(m.homeTeam, 'left')}
          ${center}
          ${teamHtml(m.awayTeam, 'right')}
        </div>`;
      });
      html += `</div>`;
    }
    container.innerHTML = html;
  }

  async function load() {
    if (_data) { render(_data); return; }
    const container = document.getElementById('matches-body');
    container.innerHTML = '<div class="spinner"></div>';
    try {
      _data = await Api.upcoming();
      render(_data);
    } catch(e) {
      container.innerHTML = `<div class="empty-state"><div class="empty-icon">⚠️</div><div class="empty-msg">Failed to load</div></div>`;
    }
  }

  function invalidate() { _data = null; }

  return { load, invalidate };
})();
