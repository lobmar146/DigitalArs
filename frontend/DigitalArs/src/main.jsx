import { createRoot } from 'react-dom/client'
import './index.css'
import { BrowserRouter } from 'react-router-dom'
import App from './App.jsx'
import ElementosGlobalesProvider from './context/ElementosGlobales'
import AuthProvider from './context/AuthProvider'
import NotificacionesProvider from './context/NotificacionesProvider'
import { api } from './context/api'

// Si la API publicada estaba dormida, empieza a despertar mientras el visitante mira la portada,
// y no recién cuando intenta ingresar. /health no consulta la base, así que no la despierta.
api.get('/health').catch(() => {})

// NotificacionesProvider va DEBAJO de AuthProvider, porque necesita la sesión, y POR ENCIMA de
// App, porque la campana vive en el encabezado y el aviso puede llegar en cualquier pantalla.
createRoot(document.getElementById('root')).render(
  <BrowserRouter>
    <ElementosGlobalesProvider>
      <AuthProvider>
        <NotificacionesProvider><App /></NotificacionesProvider>
      </AuthProvider>
    </ElementosGlobalesProvider>
  </BrowserRouter>
)
