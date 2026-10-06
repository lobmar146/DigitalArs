import './App.css'

import Header from './components/Header/Header'
import Main from './components/Main/Main'
import Footer from './components/Footer/Footer'

import Box from '@mui/material/Box'
import { ThemeProvider } from '@mui/material/styles'
import CssBaseline from '@mui/material/CssBaseline'
import GlobalStyles from '@mui/material/GlobalStyles'

import { useContext } from 'react'
import { useLocation } from 'react-router-dom'
import { ElementosGlobales } from './context/ElementosGlobales'
import ScrollTopButton from './components/Home/ScrollTopButton'
import FondoDelSitio from './components/Fondo/FondoDelSitio'
import BarraLateral, { ANCHO_DE_LA_BARRA_LATERAL } from './components/Navegacion/BarraLateral'
import EncabezadoDeSesion from './components/Navegacion/EncabezadoDeSesion'
import BarraInferior from './components/Navegacion/BarraInferior'
import AvisoDeNotificacion from './components/Notificaciones/AvisoDeNotificacion'
import AvisoServidorDespertando from './components/Comunes/AvisoServidorDespertando'
import useShell from './hooks/useShell'
import { SHELL_ESCRITORIO, SHELL_MOBILE } from './routes/shellUtils'

// Deja lugar abajo para que la barra inferior fija (y el botón de QR que sobresale) no tape el
// final de la página.
const ESPACIO_DE_LA_BARRA_INFERIOR = 'calc(88px + env(safe-area-inset-bottom))'

// En la vista mobile se oculta la barra de scroll de la página: en el teléfono no se usa (se
// desliza con el dedo) y en una ventana angosta de escritorio ocupaba lugar al costado. La
// página sigue deslizándose igual; solo deja de dibujarse la barra. En escritorio se mantiene.
const SIN_BARRA_DE_SCROLL = {
  html: { scrollbarWidth: 'none' },
  'html::-webkit-scrollbar': { display: 'none' }
}

// Arma la estructura de la página. Está separado de App porque necesita leer el tema propio
// (los breakpoints que consulta useShell), y ese tema recién existe DENTRO del ThemeProvider.
function EstructuraDeLaPagina() {
  const shell = useShell()
  const { pathname } = useLocation()

  if (shell === SHELL_MOBILE) {
    return (
      <>
        <GlobalStyles styles={SIN_BARRA_DE_SCROLL} />

        <EncabezadoDeSesion />

        <Box sx={{ pb: ESPACIO_DE_LA_BARRA_INFERIOR }}>
          <Main />
        </Box>

        <BarraInferior />
      </>
    )
  }

  if (shell === SHELL_ESCRITORIO) {
    return (
      // La barra lateral es un Drawer permanente, así que reserva su propio ancho con
      // flexShrink: 0 y la columna de contenido se queda con el resto. minWidth: 0 es lo que
      // deja que el contenido se encoja en vez de desbordar la ventana: sin eso, una tabla
      // ancha (el historial) empuja la barra fuera de la pantalla.
      <Box sx={{ display: 'flex', minHeight: '100vh' }}>
        <BarraLateral />

        <Box sx={{ flexGrow: 1, minWidth: 0, maxWidth: `calc(100% - ${ANCHO_DE_LA_BARRA_LATERAL}px)` }}>
          <EncabezadoDeSesion />
          <Main />
        </Box>

        <ScrollTopButton />
      </Box>
    )
  }

  return (
    <>
      <Header />

      <Main />

      <Footer />
      {pathname !== '/' && <ScrollTopButton />}
    </>
  )
}

function App() {
  const { theme } = useContext(ElementosGlobales)

  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />

      {/* Los rayos van detrás de todo. La app entera se envuelve en una capa con zIndex 1 para
          quedar por encima: sin eso, el fondo es un elemento posicionado y se dibujaría ARRIBA
          del contenido en flujo, que no lleva z-index.
          `position: relative` en la envoltura no afecta a la barra inferior ni a la lateral, que
          son `fixed`: solo un transform, un filter o un will-change crean un marco nuevo para
          ellas, y esto no es ninguno de los tres. */}
      <FondoDelSitio />

      <Box sx={{ position: 'relative', zIndex: 1 }}>
        <EstructuraDeLaPagina />
      </Box>

      <AvisoDeNotificacion />
      <AvisoServidorDespertando />
    </ThemeProvider>
  )
}

export default App
