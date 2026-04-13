const StandingsTab = (() => {
  let _data = null;

  function emblemOrPlaceholder(url, name, cls) {
    if (url) return `<img class="${cls}" src="${url}" alt="${name}" onerror="this.style.display='none';this.nextElementSibling.style.display='flex'">
                     <div class="${cls.replace('emblem','emblem-placeholder')}" style="display:none">${(name||'?').substring(0,3).toUpperCase()}</div>`;
    return `<div class="${cls.replace('emblem','emblem-placeholder')}">${(name||'?').substring(0,3).toUpperCase()}</div>`;
  }

  function rankClass(r, total) {
    if (r <= 4) return 'top4';
    if (r === 5 || r === 6) return 'europa';
    if (r >= total - 2) return 'relegation';
    return '';
  }

  function render(data) {
    const container = document.getElementById('standings-body');
    if (!data || data.length === 0) {
      container.innerHTML = `<div class="empty-state"><div class="empty-icon">📊</div><div class="empty-msg">No data</div></div>`;
      return;
    }
    const total = data.length;
    const t = i18n.t.bind(i18n);
    const rows = data.map(s => {
      const rc  = rankClass(s.rank, total);
      const gd  = s.goalDifference >= 0 ? `+${s.goalDifference}` : s.goalDifference;
      return `<tr onclick="StandingsTab.openTeam(${s.teamId},'${escHtml(s.teamName)}','${escHtml(s.emblemUrl||'')}')">
        <td><span class="rank-badge ${rc}">${s.rank}</span></td>
        <td>
          <div class="team-cell">
            ${emblemOrPlaceholder(s.emblemUrl, s.shortName, 'team-emblem')}
            <span class="team-name-full">${escHtml(s.teamName)}</span>
            <span class="team-name-short">${escHtml(s.shortName)}</span>
          </div>
        </td>
        <td>${s.played}</td>
        <td>${s.won}</td>
        <td>${s.drawn}</td>
        <td>${s.lost}</td>
        <td>${gd}</td>
        <td class="pts-bold">${s.points}</td>
      </tr>`;
    }).join('');

    container.innerHTML = `
      <table class="standings-table">
        <thead>
          <tr>
            <th>${t('col.pos')}</th>
            <th>${t('col.club')}</th>
            <th>${t('col.p')}</th>
            <th>${t('col.w')}</th>
            <th>${t('col.d')}</th>
            <th>${t('col.l')}</th>
            <th>${t('col.gd')}</th>
            <th>${t('col.pts')}</th>
          </tr>
        </thead>
        <tbody>${rows}</tbody>
      </table>`;
  }

  async function load() {
    if (_data) { render(_data); return; }
    const container = document.getElementById('standings-body');
    container.innerHTML = '<div class="spinner"></div>';
    try {
      _data = await Api.standings();
      render(_data);
    } catch(e) {
      const msg = e.data?.error || e.message || 'Failed to load';
      container.innerHTML = `<div class="empty-state"><div class="empty-icon">⚠️</div><div class="empty-msg">${msg}</div></div>`;
    }
  }

  function openTeam(teamId, name, emblemUrl) {
    TeamModal.open(teamId, name, emblemUrl);
  }

  return { load, openTeam };
})();
