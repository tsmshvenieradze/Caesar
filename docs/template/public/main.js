// Version picker for the versioned site layout: /<X.Y>/... and /latest/..., listed in /versions.json.
// Hidden when the page is not served from that layout (e.g. `docfx --serve`) or versions.json is unavailable.

function versionSegment(pathname) {
  const first = pathname.split('/').filter(Boolean)[0]
  return first === 'latest' || /^\d+\.\d+$/.test(first ?? '') ? first : null
}

async function switchVersion(target, current) {
  const rest = location.pathname.slice(`/${current}/`.length)
  const candidate = `/${target}/${rest}`
  try {
    const response = await fetch(candidate, { method: 'HEAD' })
    location.href = response.ok ? candidate : `/${target}/`
  } catch {
    location.href = `/${target}/`
  }
}

async function addVersionPicker() {
  const current = versionSegment(location.pathname)
  const brand = document.querySelector('a.navbar-brand')
  if (!current || !brand) {
    return
  }

  let versions
  try {
    const response = await fetch('/versions.json', { cache: 'no-cache' })
    if (!response.ok) {
      return
    }
    versions = await response.json()
  } catch {
    return
  }
  if (!Array.isArray(versions) || versions.length === 0) {
    return
  }

  const latest = versions.find(v => v.latest)?.version
  const selected = current === 'latest' ? latest : current
  const select = document.createElement('select')
  select.className = 'form-select form-select-sm caesar-version'
  select.setAttribute('aria-label', 'Documentation version')
  for (const { version } of versions) {
    select.add(new Option(version === latest ? `${version} (latest)` : version, version, false, version === selected))
  }
  select.addEventListener('change', () => switchVersion(select.value === latest ? 'latest' : select.value, current))
  brand.after(select)
}

export default {
  defaultTheme: 'dark',
  iconLinks: [
    { icon: 'github', href: 'https://github.com/tsmshvenieradze/Caesar', title: 'GitHub' },
  ],
  start: () => {
    addVersionPicker()
  },
}
