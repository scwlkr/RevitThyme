# -*- coding: utf-8 -*-
"""Routes supplies ExternalEvent execution for handlers that accept uiapp."""
from pyrevit import routes
from pyrevit.userconfig import user_config
from revitthyme.operations import execute


def register():
    # Never broaden the existing listener. Refuse registration on an unsafe host.
    host = user_config.routes_host
    if host != '127.0.0.1':
        raise ValueError('RevitThyme requires Routes bound to 127.0.0.1')
    api = routes.API('revitthyme')

    @api.route('/status/', methods=['POST'])
    def suite_status(request, uiapp):
        return execute('revitthyme_status', uiapp, request.data)

    @api.route('/timberfold/inspect/', methods=['POST'])
    def timberfold_inspect(request, uiapp):
        return execute('timberfold_inspect', uiapp, request.data)

    @api.route('/preissue/check/', methods=['POST'])
    def preissue_check(request, uiapp):
        return execute('preissue_check', uiapp, request.data)

    return api
