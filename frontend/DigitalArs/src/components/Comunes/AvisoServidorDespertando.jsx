import { useSyncExternalStore } from 'react'
import Alert from '@mui/material/Alert'
import CircularProgress from '@mui/material/CircularProgress'
import Snackbar from '@mui/material/Snackbar'

import { servidorDespertando, suscribirseAlServidor } from '../../context/api'
import useShell from '../../hooks/useShell'
import { SHELL_MOBILE } from '../../routes/shellUtils'

// Aparece mientras algún pedido a la API viene demorado (ver reintentos en api.js) y se va solo
// cuando responde. Está pensado para quien entra por primera vez, por ejemplo desde el
// portfolio: le explica la espera en lugar de dejarlo frente a una pantalla quieta.
//
// Usa la misma posición que AvisoDeNotificacion: abajo en mobile, por encima de la barra
// inferior, y arriba al centro en escritorio.
export default function AvisoServidorDespertando() {
  const despertando = useSyncExternalStore(suscribirseAlServidor, servidorDespertando)
  const esVistaMobile = useShell() === SHELL_MOBILE

  const posicion = esVistaMobile
    ? { vertical: 'bottom', horizontal: 'center' }
    : { vertical: 'top', horizontal: 'center' }

  return (
    <Snackbar
      open={despertando}
      anchorOrigin={posicion}
      sx={esVistaMobile ? { bottom: { xs: 96 } } : undefined}
    >
      <Alert
        severity="info"
        variant="filled"
        icon={<CircularProgress size={20} color="inherit" />}
        role="status"
      >
        Despertando el servidor… La demo corre en un plan gratuito que se apaga cuando nadie la
        usa: la primera carga puede tardar hasta un minuto.
      </Alert>
    </Snackbar>
  )
}
