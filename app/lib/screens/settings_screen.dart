import 'package:flutter/material.dart';

import '../api/api_client.dart';
import '../theme.dart';

class SettingsScreen extends StatefulWidget {
  const SettingsScreen({super.key});

  @override
  State<SettingsScreen> createState() => _SettingsScreenState();
}

class _SettingsScreenState extends State<SettingsScreen> {
  late final TextEditingController _urlCtrl;
  bool _testing = false;
  bool? _healthy;

  @override
  void initState() {
    super.initState();
    _urlCtrl = TextEditingController(text: ApiClient.instance.baseUrl);
  }

  @override
  void dispose() {
    _urlCtrl.dispose();
    super.dispose();
  }

  Future<void> _saveAndTest() async {
    await ApiClient.saveBaseUrl(_urlCtrl.text);
    setState(() {
      _testing = true;
      _healthy = null;
    });
    final ok = await ApiClient.instance.health();
    if (!mounted) return;
    setState(() {
      _testing = false;
      _healthy = ok;
    });
    if (ok) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Сохранено, API отвечает')),
      );
    } else {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('API не отвечает по этому адресу')),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('НАСТРОЙКИ')),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          const Text(
            'АДРЕС API',
            style: TextStyle(
              fontSize: 11,
              letterSpacing: 1.0,
              color: AppColors.textMuted,
              fontWeight: FontWeight.w700,
            ),
          ),
          const SizedBox(height: 8),
          TextField(
            controller: _urlCtrl,
            keyboardType: TextInputType.url,
            decoration: const InputDecoration(
              hintText: 'http://localhost:5178',
              prefixIcon: Icon(Icons.dns_outlined, size: 20),
            ),
          ),
          const SizedBox(height: 8),
          Text(
            'Windows: http://localhost:5178\nAndroid-эмулятор: http://10.0.2.2:5178\nТелефон: http://<IP ПК>:5178',
            style: const TextStyle(
              fontFamily: 'RobotoMono',
              fontSize: 11,
              color: AppColors.textMuted,
            ),
          ),
          const SizedBox(height: 16),
          FilledButton.icon(
            onPressed: _testing ? null : _saveAndTest,
            style: FilledButton.styleFrom(backgroundColor: AppColors.accent),
            icon: _testing
                ? const SizedBox(
                    width: 16,
                    height: 16,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : const Icon(Icons.save_outlined, size: 18),
            label: Text(_testing ? 'ПРОВЕРЯЕМ…' : 'СОХРАНИТЬ И ПРОВЕРИТЬ'),
          ),
          const SizedBox(height: 12),
          if (_healthy == true)
            const Row(
              children: [
                Icon(Icons.check_circle, color: AppColors.ok, size: 18),
                SizedBox(width: 8),
                Text('API доступен', style: TextStyle(color: AppColors.ok)),
              ],
            )
          else if (_healthy == false)
            const Row(
              children: [
                Icon(Icons.cancel, color: AppColors.bad, size: 18),
                SizedBox(width: 8),
                Text('API недоступен', style: TextStyle(color: AppColors.bad)),
              ],
            ),
          const SizedBox(height: 24),
          const Divider(),
          const SizedBox(height: 16),
          const Text(
            'KLEVO · v0.5 (Фаза 5)\n\nПрогноз клёва, правила рыболовства и фото-определитель вида. '
            'Данные — пилотный регион.',
            style: TextStyle(fontFamily: 'RobotoMono', fontSize: 11, color: AppColors.textMuted),
          ),
        ],
      ),
    );
  }
}
