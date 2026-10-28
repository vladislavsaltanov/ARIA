package aria.monitor.app

import aria.monitor.core.Pairing

internal class ActiveHost {
    @Volatile
    var pairing: Pairing? = null
}
